// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using localRAG;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.DataFormats.Text;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Extensions;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory.Prompts;

namespace Microsoft.KernelMemory.Handlers;

public sealed class ManageTagHandler : IPipelineStepHandler
{
    private const int MinLength = 30;

    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ILogger<ManageTagHandler> _log;
    private readonly string _tocPrompt;

    /// <inheritdoc />
    public string StepName { get; }

    /// <summary>
    /// Handler responsible for generating a summary of each file in a document.
    /// The summary serves as an additional partition, aka it's part of the synthetic
    /// data generated for documents, in order to increase hit ratio and Q/A quality.
    /// </summary>
    /// <param name="stepName">Pipeline step for which the handler will be invoked</param>
    /// <param name="orchestrator">Current orchestrator used by the pipeline, giving access to content and other helps.</param>
    /// <param name="promptProvider">Class responsible for providing a given prompt</param>
    /// <param name="loggerFactory">Application logger factory</param>
    public ManageTagHandler(
        string stepName,
        IPipelineOrchestrator orchestrator,
        ILoggerFactory? loggerFactory = null)
    {
        this.StepName = stepName;
        this._orchestrator = orchestrator;

        this._log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<ManageTagHandler>();

        this._log.LogInformation("Handler '{0}' ready", stepName);
    }

    /// <inheritdoc />
    public async Task<(ReturnType returnType, DataPipeline updatedPipeline)> InvokeAsync(
        DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        this._log.LogDebug("Handling Tags, pipeline '{0}/{1}'", pipeline.Index, pipeline.DocumentId);

        var tags = new TagCollection();
        foreach (DataPipeline.FileDetails uploadedFile in pipeline.Files)
        {
            foreach (KeyValuePair<string, DataPipeline.GeneratedFileDetails> generatedFile in uploadedFile.GeneratedFiles)
            {
                var file = generatedFile.Value;

                if (file.AlreadyProcessedBy(this))
                {
                    this._log.LogTrace("File {0} already processed by this handler", file.Name);
                    continue;
                }

                foreach (var tag in pipeline.Tags)
                {
                    if (tags.Keys.Any(k => k == tag.Key))
                    {
                        // all values of tag with this key to the tags list with the same key, with distinct values
                        tags[tag.Key] = tags[tag.Key].Union(tag.Value).ToList();
                    }
                    else
                    {
                        tags.Add(tag);
                    }
                }
                file.MarkProcessedBy(this);
            }

        }

        // print whole tag list from pipeline
        Console.WriteLine("Tags from pipeline:");
        foreach (var tag in pipeline.Tags)
        {
            Console.WriteLine($"Key: {tag.Key}");
            Console.WriteLine(" - ["+string.Join(", ", tag.Value)+"]");
        }

        var tagsString = JsonSerializer.Serialize(tags);
        var tagsData = new BinaryData(tagsString);
        var tagFile = new DataPipeline.GeneratedFileDetails
        {
            Id = Helpers.HashThis(tagsString),
            Name = "tags.json",
            ArtifactType = DataPipeline.ArtifactTypes.SyntheticData,
            Size = tagsString.Length,
            MimeType = MimeTypes.Json,
            ContentSHA256 = CalculateSHA256(tagsData),
            Tags = tags.Clone().AddSyntheticTag("TAGS"),
        };

        return (ReturnType.Success, pipeline);
    }
    public static string CalculateSHA256(BinaryData binaryData)
    {
        byte[] byteArray = SHA256.HashData(binaryData.ToMemory().Span);
        return Convert.ToHexString(byteArray).ToLowerInvariant();
    }
    private async Task<(string summary, bool skip)> GetTagsAsync(string content, IContext context)
    {
        ITextGenerator textGenerator = this._orchestrator.GetTextGenerator();
        int contentLength = textGenerator.CountTokens(content);
        this._log.LogTrace("Size of the content to summarize: {0} tokens", contentLength);

        // If the content is less than 30 tokens don't do anything and move on.
        if (contentLength < MinLength)
        {
            this._log.LogWarning("Content is too short to summarize ({0} tokens), nothing to do", contentLength);
            return (content, true);
        }

        // By default, the goal is to summarize to 50% of the model capacity (or less)
        int targetSummarySize = textGenerator.MaxTokenTotal / 2;

        // Allow to override the target goal using context arguments
        var customTargetSummarySize = context.GetCustomSummaryTargetTokenSizeOrDefault(-1);
        if (customTargetSummarySize > 0)
        {
            if (customTargetSummarySize > textGenerator.MaxTokenTotal / 2)
            {
                throw new ArgumentOutOfRangeException(
                    $"Custom summary size is too large, the max value allowed is {textGenerator.MaxTokenTotal / 2} (50% of the model capacity)");
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(customTargetSummarySize, 15);
            targetSummarySize = customTargetSummarySize;
        }

        this._log.LogTrace("Target goal: summary max size <= {0} tokens", targetSummarySize);

        // By default, use 25% of the previous paragraph when summarizing a paragraph
        int maxTokensPerParagraph = textGenerator.MaxTokenTotal / 4;

        // When splitting text in sentences take 100..500 tokens
        // If possible allow 50% of the paragraph size, aka 12.5% of the model capacity.
        int maxTokensPerLine = Math.Min(Math.Max(100, maxTokensPerParagraph / 2), 500);

        // By default, use 6.2% of the model capacity for overlapping tokens
        int overlappingTokens = maxTokensPerLine / 2;

        // Allow to override the number of overlapping tokens using context arguments
        var customOverlappingTokens = context.GetCustomSummaryOverlappingTokensOrDefault(-1);
        if (customOverlappingTokens >= 0)
        {
            if (customOverlappingTokens > maxTokensPerLine / 2)
            {
                throw new ArgumentOutOfRangeException(
                    $"Custom number of overlapping tokens is too large, the max value allowed is {maxTokensPerLine / 2}");
            }

            overlappingTokens = customOverlappingTokens;
        }

        this._log.LogTrace("Overlap setting: {0} tokens", overlappingTokens);

        // Summarize at least once
        var done = false;

        var summarizationPrompt = context.GetCustomSummaryPromptOrDefault(this._tocPrompt);

        // If paragraphs overlap, we need to dedupe the content, e.g. run at least one summarization call on the entire content
        var overlapToRemove = overlappingTokens > 0;

        // Since the summary is meant to be shorter than the content, reserve 50% of the model
        // capacity for input and 50% for output (aka the summary to generate)
        int maxInputTokens = textGenerator.MaxTokenTotal / 2;

        // After the first run (after overlaps have been introduced), check if the summarization is causing the content to grow
        bool firstRun = overlapToRemove;
        int previousLength = contentLength;
        while (!done)
        {
            var paragraphs = new List<string>();

            // If the content fits into half the model capacity, use a single paragraph
            if (contentLength <= maxInputTokens)
            {
                overlapToRemove = false;
                paragraphs.Add(content);
            }
            else
            {
#pragma warning disable KMEXP00 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                List<string> lines = TextChunker.SplitPlainTextLines(content, maxTokensPerLine: maxTokensPerLine);
                paragraphs = TextChunker.SplitPlainTextParagraphs(lines, maxTokensPerParagraph: maxTokensPerParagraph, overlapTokens: overlappingTokens);
#pragma warning restore KMEXP00 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            }

            this._log.LogTrace("Paragraphs to summarize: {0}", paragraphs.Count);
            var newContent = new StringBuilder();
            for (int index = 0; index < paragraphs.Count; index++)
            {
                string paragraph = paragraphs[index];
                this._log.LogTrace("Summarizing paragraph {0}", index);

                var filledPrompt = summarizationPrompt.Replace("{{$input}}", paragraph, StringComparison.OrdinalIgnoreCase);
                await foreach (string token in textGenerator.GenerateTextAsync(filledPrompt, new TextGenerationOptions()).ConfigureAwait(false))
                {
                    newContent.Append(token);
                }

                newContent.AppendLine();
            }

            content = newContent.ToString();
            contentLength = textGenerator.CountTokens(content);

            // If the compression fails, stop, log an error, and save the content generated this far.
            if (!firstRun && contentLength >= previousLength)
            {
                this._log.LogError(
                    "Summarization stopped, the content is not getting shorter: {0} tokens => {1} tokens. The summary has been saved but is longer than requested.",
                    previousLength, contentLength);
                return (content, true);
            }

            this._log.LogTrace("Summary length: {0} => {1}", previousLength, contentLength);
            previousLength = contentLength;

            firstRun = false;
            done = !overlapToRemove && (contentLength <= targetSummarySize);
        }

        return (content, true);
    }
}