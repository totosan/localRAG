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
}