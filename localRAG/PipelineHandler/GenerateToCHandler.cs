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

public sealed class GenerateTocHandler : IPipelineStepHandler
{
    private const int MinLength = 30;

    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ILogger<GenerateTocHandler> _log;
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
    public GenerateTocHandler(
        string stepName,
        IPipelineOrchestrator orchestrator,
        IPromptProvider? promptProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        this.StepName = stepName;
        this._orchestrator = orchestrator;

#pragma warning disable KMEXP00 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        promptProvider ??= new EmbeddedPromptProvider();
#pragma warning restore KMEXP00 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        this._tocPrompt = """
        Analysiere den Inhalt des Dokuments, um die relevanten Kategorien zu identifizieren. Berücksichtige, dass das Dokument, wenn es verstaut ist, von jemanden wieder gefunden werden soll. Beantworte die Aufgabe unter Berücksichtigung einer effizienten Suche.

        Anschließend generiere mir zu diesem Dokument drei TAGs, mit denen ich es in einer Bibliothek organisieren kann.

        Beachte folgendes:
        (Die TAGs sind hierarchisch)
        TAG 1: allgemeine Kategorie (welchen Bereich deckt das Dokument ab? Arbeit und Beruf, Fahrzeuge, Finanzen und Steuern, Gesundheit, Persönliche Dokumente, Versicherungen, Wohnen oder Immobilien, IT)
        TAG 2: Unterkategorie (aus den Allgemeinen)
        TAG 3: speziell - beschreibende Kategorie
        --------------
        Content: 
        {{$input}}

        ---------------

        OUTPUT:
        gib lediglich die TAGs sortiert von allgemein nach speziell aus. Verwende das JSON Format. Verwende nur die Englische Sprache!
        e.g. you found following tags:
        TAG1: Finance and Taxes
        TAG2: FinTech
        TAG3: Softwaredevelopment and agile

        -> Transforms to JSON:
        {
        "Finance and Taxes": ["FinTech"],
        "FinTech": ["Software Development“,“agile"]
        }
        Vermeide Beschreibungen, Erläuterungen, Analyseergebnisse. Gib JSON als Antwort zurück.
        """;

        this._log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<GenerateTocHandler>();

        this._log.LogInformation("Handler '{0}' ready", stepName);
    }

    /// <inheritdoc />
    public async Task<(ReturnType returnType, DataPipeline updatedPipeline)> InvokeAsync(
        DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        this._log.LogDebug("Generating ToC, pipeline '{0}/{1}'", pipeline.Index, pipeline.DocumentId);

        foreach (DataPipeline.FileDetails uploadedFile in pipeline.Files)
        {
            // Track new files being generated (cannot edit originalFile.GeneratedFiles while looping it)
            Dictionary<string, DataPipeline.GeneratedFileDetails> tocFiles = [];

            foreach (KeyValuePair<string, DataPipeline.GeneratedFileDetails> generatedFile in uploadedFile.GeneratedFiles)
            {
                var file = generatedFile.Value;

                if (file.AlreadyProcessedBy(this))
                {
                    this._log.LogTrace("File {0} already processed by this handler", file.Name);
                    continue;
                }

                // Summarize only the original content
                if (file.ArtifactType != DataPipeline.ArtifactTypes.ExtractedText)
                {
                    this._log.LogTrace("Skipping file {0}", file.Name);
                    continue;
                }

                switch (file.MimeType)
                {
                    case MimeTypes.PlainText:
                    case MimeTypes.MarkDown:
                        this._log.LogDebug("Generating ToC file {0}", file.Name);
                        string content = (await this._orchestrator.ReadFileAsync(pipeline, file.Name, cancellationToken).ConfigureAwait(false)).ToString();
                        (string summary, bool success) = await this.GetTagsAsync(content, pipeline.GetContext()).ConfigureAwait(false);
                        if (success)
                        {
                            try
                            {
                                // remove ```json from the summary
                                summary = summary.Replace("```json", "").Replace("```", "").Trim();
                                // read the summary, which is a string containing json and convert it to a dictionary
                                var tags = JsonSerializer.Deserialize<IDictionary<string, List<string?>>>(summary);
                                foreach (var tag in tags)
                                {
                                    pipeline.Tags.Add(tag.Key, tag.Value);
                                }
                            }
                            catch (System.Text.Json.JsonException e)
                            {
                                _log.LogError("Error while deserializing the tag list: {0}", e.Message);
                            }
                        }

                        break;

                    default:
                        this._log.LogWarning("File {0} cannot be summarized, type not supported", file.Name);
                        continue;
                }

                file.MarkProcessedBy(this);
            }

            // Add new files to pipeline status
            foreach (var file in tocFiles)
            {
                file.Value.MarkProcessedBy(this);
                uploadedFile.GeneratedFiles.Add(file.Key, file.Value);
            }
        }

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