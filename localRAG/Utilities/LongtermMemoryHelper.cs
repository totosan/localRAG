using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using localRAG.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;

namespace localRAG.Utilities
{
    /// <summary>
    /// 🎤 SLIDES 5, 7, 8, 9: Core RAG retrieval pipeline
    /// 
    /// This class demonstrates multiple RAG concepts:
    /// - Slide 5: Chunking strategy with adjacent chunks (overlap)
    /// - Slide 7: Dense retriever (vector search)
    /// - Slide 8: Hybrid retrieval (semantic + keyword filtering)
    /// - Slide 9: Reranking application points
    /// </summary>
    public class LongtermMemoryHelper
    {
        private static readonly IReadOnlyList<string> PipelineStepOrder = new[]
        {
            Constants.PipelineStepsExtract,
            "generate_tags",
            Constants.PipelineStepsPartition,
            Constants.PipelineStepsGenEmbeddings,
            Constants.PipelineStepsSaveRecords,
        };

        /// <summary>
        /// Loads and stores PDF, DOCX, PPTX, and image files from the specified directory into memory, extracting tags for each document.
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        /// <param name="path">The directory path to search for files.</param>
        /// <returns>A TagCollection containing tags from the last processed file.</returns>
        public static async Task<TagCollection> LoadAndStorePdfFromPathAsync(IKernelMemory memoryConnector, string path)
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var context = new RequestContext();
            var tags = new TagCollection();

            TraceLogger.Log($"LoadAndStorePdfFromPathAsync invoked for '{path}' with {files.Count} candidate files.");

            if (files.Count == 0)
            {
                TraceLogger.Log($"No supported documents found under '{path}'.", echoToConsole: true, consoleMarkup: $"[yellow]No supported documents found in[/] [underline]{Markup.Escape(path)}[/].");
                AnsiConsole.MarkupLine($"[yellow]No supported documents found in[/] [underline]{path}[/].");
                return tags;
            }

            var stepsPerDocument = PipelineStepOrder.Count;

            TraceLogger.Log($"Beginning import for {files.Count} documents (steps/doc: {stepsPerDocument}).");

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async progressCtx =>
                {
                    var importTask = progressCtx.AddTask("[green]Importing documents[/]", maxValue: files.Count * stepsPerDocument);
                    var processedDocuments = 0;

                    foreach (var file in files)
                    {
                        var baseStepValue = processedDocuments * stepsPerDocument;
                        var fileId = Helpers.HashThis(file);
                        var fileName = Path.GetFileName(file);
                        var saved = await memoryConnector.IsDocumentReadyAsync(fileId);

                        TraceLogger.Log($"[{fileName}] Starting processing (fileId={fileId}, saved={saved}).");

                        importTask.Description = saved
                            ? $"[grey]Up-to-date[/] {fileName}"
                            : $"[cyan]Importing[/] {fileName}";

                        TaskCompletionSource<bool>? monitorCompletion = null;
                        Task monitorTask = Task.CompletedTask;

                        if (!saved)
                        {
                            monitorCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            monitorTask = MonitorPipelineStepsAsync(
                                memoryConnector,
                                fileId,
                                fileName,
                                importTask,
                                baseStepValue,
                                monitorCompletion.Task);

                            var importStopwatch = Stopwatch.StartNew();
                            try
                            {
                                TraceLogger.Log($"[{fileName}] ImportDocumentAsync queued.");
                                await memoryConnector.ImportDocumentAsync(new Document(fileId)
                                        .AddFile(file),
                                        steps: PipelineStepOrder.ToArray(),
                                        context: context);
                                importStopwatch.Stop();
                                TraceLogger.Log($"[{fileName}] ImportDocumentAsync finished in {importStopwatch.Elapsed}." , echoToConsole: false);
                                AnsiConsole.MarkupLine($"[green]queued[/] {file}");
                            }
                            catch (Exception ex)
                            {
                                importStopwatch.Stop();
                                TraceLogger.Log($"[{fileName}] ImportDocumentAsync failed after {importStopwatch.Elapsed}: {ex.Message}", echoToConsole: true, consoleMarkup: $"[red]Error processing[/] {Markup.Escape(file)}: {Markup.Escape(ex.Message)}");
                                AnsiConsole.MarkupLine($"[red]Error processing[/] {Markup.Escape(file)}: {Markup.Escape(ex.Message)}");
                                throw;
                            }
                            finally
                            {
                                monitorCompletion.TrySetResult(true);
                                await monitorTask;
                            }
                        }
                        else
                        {
                            importTask.Value = baseStepValue + stepsPerDocument;
                        }

                        importTask.Value = baseStepValue + stepsPerDocument;
                        importTask.Description = saved
                            ? $"[grey]Up-to-date[/] {fileName}"
                            : $"[green]Completed[/] {fileName}";
                        processedDocuments++;

                        var newTags = await GetTagsFromDocumentById(memoryConnector, file, fileId);
                        foreach (var kvp in newTags)
                        {
                            if (tags.ContainsKey(kvp.Key))
                            {
                                if (tags[kvp.Key] == null) tags[kvp.Key] = new List<string?>();
                                if (kvp.Value != null)
                                {
                                    foreach (var val in kvp.Value)
                                    {
                                        if (!tags[kvp.Key]!.Contains(val))
                                        {
                                            tags[kvp.Key]!.Add(val);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                tags.Add(kvp.Key, kvp.Value ?? new List<string?>());
                            }
                        }

                        var fileDisplay = Markup.Escape(file ?? "unknown");
                        var tagDisplay = newTags.Count == 0
                            ? "[grey]none[/]"
                            : string.Join(", ", newTags.Select(kvp =>
                                $"{Markup.Escape(kvp.Key)}: {Markup.Escape(string.Join("/", kvp.Value ?? []))}"));

                        AnsiConsole.MarkupLine($"[dim]Tags for[/] {fileDisplay}: {tagDisplay}");
                        TraceLogger.Log($"[{fileName}] Tags => {tagDisplay.Replace("[grey]none[/]", "none")}");
                    }
                });

            return tags;
        }

        private static async Task MonitorPipelineStepsAsync(
            IKernelMemory memoryConnector,
            string fileId,
            string fileName,
            ProgressTask progressTask,
            double baseValue,
            Task completionSignal)
        {
            var stepsPerDoc = PipelineStepOrder.Count;
            var monitorWatch = Stopwatch.StartNew();
            string? lastStep = null;
            int lastCompleted = -1;
            int lastRemaining = stepsPerDoc;

            while (true)
            {
                if (completionSignal.IsCompleted)
                {
                    break;
                }

                int completedSteps = 0;
                int remainingSteps = stepsPerDoc;

                try
                {
                    var status = await memoryConnector.GetDocumentStatusAsync(fileId);
                    completedSteps = status?.CompletedSteps?.Count ?? 0;
                    remainingSteps = status?.RemainingSteps?.Count ?? Math.Max(stepsPerDoc - completedSteps, 0);
                }
                catch
                {
                    // Status may not be available immediately while the pipeline warms up.
                }

                var stepIndex = Math.Clamp(completedSteps, 0, Math.Max(stepsPerDoc - 1, 0));
                var currentStep = completedSteps >= stepsPerDoc && remainingSteps == 0
                    ? "finalizing"
                    : PipelineStepOrder[stepIndex];

                progressTask.Description = $"[cyan]{currentStep}[/] {fileName}";
                var newValue = baseValue + Math.Min(completedSteps, stepsPerDoc);
                progressTask.Value = Math.Min(newValue, progressTask.MaxValue);

                if (currentStep != lastStep || completedSteps != lastCompleted)
                {
                    TraceLogger.Log($"[{fileName}] step={currentStep} completed={completedSteps} remaining={remainingSteps} elapsed={monitorWatch.Elapsed}");
                    lastStep = currentStep;
                    lastCompleted = completedSteps;
                    lastRemaining = remainingSteps;
                }

                if (completedSteps >= stepsPerDoc && remainingSteps == 0)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            TraceLogger.Log($"[{fileName}] pipeline monitor finished (completed={lastCompleted}, remaining={lastRemaining}) after {monitorWatch.Elapsed}.");
        }


        /// <summary>
        /// Retrieves tags from a document by its ID and prints them to the console.
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        /// <param name="file">The file name (optional, for logging).</param>
        /// <param name="fileId">The document ID.</param>
        /// <returns>A TagCollection containing the tags for the document.</returns>
        private static async Task<TagCollection> GetTagsFromDocumentById(IKernelMemory memoryConnector, string? file, string fileId)
        {
            TagCollection tags = new TagCollection();
            var pipeline = await memoryConnector.GetDocumentStatusAsync(fileId);
            // Console.WriteLine($"Document {file} is being processed");
            //write all tags from pipleline
            foreach (var tag in pipeline.Tags)
            {
                var tagValue = string.Join(", ", tag.Value);
                Console.WriteLine($"\tTag: {tag.Key} Value: {tagValue}");
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

            return tags;
        }

        /// <summary>
        /// Retrieves the intents from the long-term memory based on the provided request.
        /// </summary>
        /// <param name="request">The user request string.</param>
        /// <param name="s_memory">The kernel memory connector instance.</param>
        /// <returns>A list of intent strings found in memory.</returns>
        public static async Task<List<string>> AskForIntentAsync(string request, IKernelMemory s_memory)
        {
            TraceLogger.Log($"[AskForIntentAsync] Searching intent index for: {request}");
            
            // DEBUG: Test if default index works
            var defaultTest = await s_memory.SearchAsync(request, index: "default", minRelevance: 0.0, limit: 3);
            TraceLogger.Log($"[AskForIntentAsync] DEBUG - Default index returned {defaultTest.Results.Count} results");
            
            // Set minRelevance to 0.0 (0%) to see ALL results regardless of relevance
            SearchResult answer = await s_memory.SearchAsync(request, index: "intent", minRelevance: 0.0, limit: 10);
            TraceLogger.Log($"[AskForIntentAsync] Found {answer.Results.Count} results in intent index");
            
            List<string> intents = new List<string>();
            foreach (Citation result in answer.Results)
            {
                var relevance = result.Partitions.FirstOrDefault()?.Relevance ?? 0;
                TraceLogger.Log($"[AskForIntentAsync] Result relevance: {relevance:F3}, text: {result.Partitions.FirstOrDefault()?.Text?.Substring(0, Math.Min(50, result.Partitions.FirstOrDefault()?.Text?.Length ?? 0))}");
                var retrievedIntents = GetTagValue(result, "intent", "none");
                var retrievedMainIntents = GetTagValue(result, "mainintent", "none");
                TraceLogger.Log($"[AskForIntentAsync] Retrieved intent tag: {retrievedIntents}, mainintent: {retrievedMainIntents}");
                
                // Check both 'intent' and 'mainintent' tags
                if (retrievedIntents != null &&
                     intents.Find(i => i == retrievedIntents) == null)
                {
                    intents.Add(retrievedIntents);
                }
                if (retrievedMainIntents != null &&
                     intents.Find(i => i == retrievedMainIntents) == null)
                {
                    intents.Add(retrievedMainIntents);
                }
            }

            return intents;
        }

        /// <summary>
        /// Retrieves long-term memory based on the provided query, optionally using intent filters and chunking.
        /// </summary>
        /// <param name="memory">The kernel memory interface to interact with.</param>
        /// <param name="query">The query string to search for in the memory.</param>
        /// <param name="asChunks">Whether to fetch the memory as chunks. Default is true.</param>
        /// <param name="intents">Optional list of intent filters.</param>
        /// <param name="keywordFilters">Optional list of keyword filters for hybrid search routing.</param>
        /// <returns>A JSON string containing the retrieved memory as a list of DocumentsSimple objects.</returns>
        public static async Task<string> GetLongTermMemory(IKernelMemory memory, string query, bool asChunks = true, List<string> intents = null, List<string> keywordFilters = null)
        {
            var importPath = Helpers.EnvVar("IMPORT_PATH") ?? "imported-documents";
            var context = new RequestContext();
            // Use a custom template for facts
            context.SetArg("custom_rag_fact_template_str", "=== Last update: {{$meta[last_update]}} ===\n{{$content}}\n");

            // Use a custom RAG prompt
            context.SetArg("custom_rag_prompt_str", """
                                                Facts:
                                                {{$facts}}
                                                ======
                                                Given only the timestamped facts above, provide a very short answer, include the relevant dates in brackets.
                                                If you don't have sufficient information, first reread the documents and try to gather the information derived from the context.
                                                Example: asking for Beitragserhöhung can also be answerd, if the text just says '...die Preise wurden zum neuen Jahr angehoben.'
                                                Second, if no reasonable context ore infromation can be found, reply with '{{$notFound}}'.
                                                
                                                Question: {{$input}}
                                                Answer:
                                                """);
            var documents = new List<DocumentsSimple>();
            if (asChunks)
            {
                // Fetch raw chunks, using KM indexes. More tokens to process with the chat history, but only one LLM request.
                List<MemoryFilter> filters = new List<MemoryFilter>();
                SearchResult memories = new SearchResult();
                
                // Build filters from both intents (categories) and keywords
                if (intents != null && intents.Count > 0)
                {
                    foreach (var intent in intents)
                    {
                        filters.Add(MemoryFilters.ByTag("intent", intent));
                    }
                }
                
                if (keywordFilters != null && keywordFilters.Count > 0)
                {
                    foreach (var keyword in keywordFilters)
                    {
                        filters.Add(MemoryFilters.ByTag("keywords", keyword));
                    }
                }

                if (filters.Count > 0)
                {
                    //filters.Add(MemoryFilters.ByDocument("EDDECB333E4D891C10661DA505D327560B40BEFC0C9254D5B3B580BF379A0008"));
                    memories = await memory.SearchAsync(query, minRelevance: 0.4, limit: 3, filters: filters, context: context);
                }
                else
                {
                    memories = await memory.SearchAsync(query, minRelevance: 0.4, limit: 3, context: context);
                }
                //List<SortedDictionary<int, Citation.Partition>> partCollection = await GetAdjacentChunks(memory, memories);
                var adjacent = await GetAdjacentChunksInMemoriesAsync(memory, memories);
                if (memories.Results.Count > 0)
                {
                    Console.WriteLine("Did a SEARCH");
                    foreach (var result in memories.Results)
                    {
                        foreach (var partition in result.Partitions)
                        {
                            // create a json object containing the document name, partition number, and text
                            var doc = new DocumentsSimple
                            {
                                DocumentId = result.DocumentId,
                                SourceName = result.SourceName,
                                FilePath = Path.GetFullPath(Path.Combine(importPath, result.SourceName)),
                                PartitionPath = Path.GetFullPath(Path.Combine("tmp-data", "default", result.DocumentId, $"{result.SourceName}.partition.{partition.PartitionNumber}.txt")),
                                PartitionNumber = partition.PartitionNumber,
                                Content = partition.Text,
                                Score = float.IsNaN(partition.Relevance) || float.IsInfinity(partition.Relevance) ? 0 : partition.Relevance
                            };
                            documents.Add(doc);
                        }
                    }

                    //return partCollection.SelectMany(p => p.Values).Aggregate("", (sum, chunk) => sum + chunk.Text + "\n").Trim();
                    //return memories.Results.SelectMany(m => m.Partitions).Aggregate("", (sum, chunk) => sum + chunk.Text + "\n").Trim();
                    
                    #region Slide 9: Reranking Application Point #1
                    
                    // RERANKING: Apply semantic reranking to improve result relevance
                    // This is where Slide 9 comes alive - show the reranking in action!
                    if (documents.Count > 1)
                    {
                        bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
                        var embeddingGenerator = Helpers.GetEmbeddingGenerator(useAzure: !useOllama);
                        documents = await Reranker.RerankAsync(query, documents, embeddingGenerator, topK: -1);
                    }
                    
                    #endregion
                    
                    return JsonSerializer.Serialize(documents);
                }
            }

            Console.WriteLine("Did an ASK");
            // Use KM to generate an answer. Fewer tokens, but one extra LLM request.
            MemoryAnswer answer = await memory.AskAsync(query, minRelevance: 0.7, context: context);
            foreach (var doc in answer.RelevantSources)
            {
                foreach (var part in doc.Partitions)
                {
                    var docsimple = new DocumentsSimple
                    {
                        DocumentId = doc.DocumentId,
                        SourceName = doc.SourceName,
                        FilePath = Path.GetFullPath(Path.Combine(importPath, doc.SourceName)),
                        PartitionPath = Path.GetFullPath(Path.Combine("tmp-data", "default", doc.DocumentId, $"{doc.SourceName}.partition.{part.PartitionNumber}.txt")),
                        PartitionNumber = part.PartitionNumber,
                        Content = part.Text,
                        Score = part.Relevance
                    };
                    documents.Add(docsimple);
                }
            }
            
            #region Slide 9: Reranking Application Point #2
            
            // RERANKING: Also apply reranking when using AskAsync
            if (documents.Count > 1)
            {
                bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
                var embeddingGenerator = Helpers.GetEmbeddingGenerator(useAzure: !useOllama);
                documents = await Reranker.RerankAsync(query, documents, embeddingGenerator, topK: -1);
            }
            
            #endregion
            
            return JsonSerializer.Serialize(documents);
        }

        /// <summary>
        /// Retrieves adjacent memory chunks for each partition in the search results.
        /// </summary>
        /// <param name="memory">The kernel memory interface.</param>
        /// <param name="memories">The search results to find adjacent chunks for.</param>
        /// <returns>A list of SearchResult objects containing adjacent partitions.</returns>
        private static async Task<List<SearchResult>> GetAdjacentChunksInMemoriesAsync(IKernelMemory memory, SearchResult memories)
        {
            var partCollection = new List<SearchResult>();
            // create a copy of memories to change its partitions independently (different object not reference)
            var copy = new SearchResult
            {
                Results = new List<Citation>(memories.Results)
            };

            foreach (var mem in memories.Results)
            {
                foreach (var part in mem.Partitions)
                {
                    var partitions = new SortedDictionary<int, Citation.Partition> { [part.PartitionNumber] = part };
                    // Filters to fetch adjacent partitions
                    var filters = new List<MemoryFilter>
                        {
                            MemoryFilters.ByDocument(mem.DocumentId).ByTag(Constants.ReservedFilePartitionNumberTag, $"{part.PartitionNumber - 1}"),
                            MemoryFilters.ByDocument(mem.DocumentId).ByTag(Constants.ReservedFilePartitionNumberTag, $"{part.PartitionNumber + 1}")
                        };

                    // Fetch adjacent partitions and add them to the sorted collection
                    partCollection.Add(await memory.SearchAsync("", filters: filters, limit: 2));
                }
            }

            // find related partitions in the copy and add the adjacent partitions according to the documentid and partition number
            for (int i = 0; i < partCollection.Count; i++)
            {
                // Add index and count checks to prevent out-of-range exceptions
                if (i >= copy.Results.Count) continue;
                if (partCollection[i]?.Results == null) continue;
                foreach (var part in partCollection[i].Results)
                {
                    foreach (var partition in part.Partitions)
                    {
                        copy.Results[i].Partitions.Add(partition);
                    }
                }
            }
            return partCollection;
        }
        /// <summary>
        /// Retrieves adjacent memory chunks for each partition in the search results (alternative method).
        /// </summary>
        /// <param name="memory">The kernel memory interface.</param>
        /// <param name="memories">The search results to find adjacent chunks for.</param>
        /// <returns>A list of sorted dictionaries mapping partition numbers to partitions.</returns>
        private static async Task<List<SortedDictionary<int, Citation.Partition>>> GetAdjacentChunks(IKernelMemory memory, SearchResult memories)
        {
            var partCollection = new List<SortedDictionary<int, Citation.Partition>>();
            foreach (var mem in memories.Results)
            {
                foreach (var part in mem.Partitions)
                {
                    var partitions = new SortedDictionary<int, Citation.Partition> { [part.PartitionNumber] = part };
                    // Filters to fetch adjacent partitions
                    var filters = new List<MemoryFilter>
                        {
                            MemoryFilters.ByDocument(mem.DocumentId).ByTag(Constants.ReservedFilePartitionNumberTag, $"{part.PartitionNumber - 1}"),
                            MemoryFilters.ByDocument(mem.DocumentId).ByTag(Constants.ReservedFilePartitionNumberTag, $"{part.PartitionNumber + 1}")
                        };

                    // Fetch adjacent partitions and add them to the sorted collection
                    SearchResult adjacentList = await memory.SearchAsync("", filters: filters, limit: 2);
                    if (adjacentList.Results.Count > 0)
                        foreach (Citation.Partition adjacent in adjacentList.Results.First().Partitions)
                        {
                            partitions[adjacent.PartitionNumber] = adjacent;
                        }
                    partCollection.Add(partitions);
                }
            }

            return partCollection;
        }

        /// <summary>
        /// Imports a list of web pages into memory, avoiding duplicates.
        /// </summary>
        /// <param name="memory">The kernel memory connector instance.</param>
        /// <param name="pages">A list of web page URLs to import.</param>
        public static async Task MemorizeWebPages(IKernelMemory memory, List<string> pages)
        {
            await memory.ImportTextAsync("We can talk about Semantic Kernel and Kernel Memory, you can ask any questions, I will try to reply using information from public documentation in Github", documentId: "help");
            foreach (var url in pages)
            {
                var id = GetUrlId(url);
                // Check if the page is already in memory, to avoid importing twice
                if (!await memory.IsDocumentReadyAsync(id))
                {
                    await memory.ImportWebPageAsync(url, documentId: id);
                }
            }
        }
        /// <summary>
        /// Generates a unique document ID for a given URL using hashing.
        /// </summary>
        /// <param name="url">The URL to hash.</param>
        /// <returns>The hashed document ID.</returns>
        public static string GetUrlId(string url)
        {
            return Helpers.HashThis(url);
        }


        /// <summary>
        /// Removes all indexes from the memory connector.
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        public static async Task RemoveAllIndexsAsync(IKernelMemory memoryConnector)
        {
            var indexes = await memoryConnector.ListIndexesAsync();
            Console.WriteLine($"Found {indexes.Count()} indexes");
            foreach (var index in indexes)
            {
                Console.WriteLine("Deleting Index: " + index.Name);
                await memoryConnector.DeleteIndexAsync(index.Name);
            }
        }


        /// <summary>
        /// Creates intent documents in memory from provided intent samples, uploading each question as a document with tags.
        /// NOTE: Uses direct IMemoryDb.UpsertAsync to bypass the KM pipeline, as Simple Vector DB requires MemoryRecord JSON format.
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        /// <param name="intentSamples">The intent samples containing categories and questions.</param>
        public static async Task CreateIntents(IKernelMemory memoryConnector, DocumentCategories intentSamples)
        {
            // Create IMemoryDb and embedding generator directly
            // We bypass the KM pipeline because Simple Vector DB needs MemoryRecord JSON format,
            // not the chunked/partitioned format that the pipeline produces
            var ollamaConfig = new Microsoft.KernelMemory.AI.Ollama.OllamaConfig
            {
                Endpoint = Helpers.EnvVar("OLLAMA_ENDPOINT"),
                TextModel = new Microsoft.KernelMemory.AI.Ollama.OllamaModelConfig(Helpers.EnvVar("OLLAMA_TEXT")),
                EmbeddingModel = new Microsoft.KernelMemory.AI.Ollama.OllamaModelConfig(Helpers.EnvVar("OLLAMA_EMBEDDING"))
            };
            
            var embeddingGen = new Microsoft.KernelMemory.AI.Ollama.OllamaTextEmbeddingGenerator(ollamaConfig, textTokenizer: new CL100KTokenizer());
            var vectorDbConfig = new Microsoft.KernelMemory.MemoryStorage.DevTools.SimpleVectorDbConfig 
            { 
                StorageType = Microsoft.KernelMemory.FileSystem.DevTools.FileSystemTypes.Disk,
                Directory = "tmp-data"
            };
#pragma warning disable KMEXP03 // Type is for evaluation purposes only
            var memoryDb = new Microsoft.KernelMemory.MemoryStorage.DevTools.SimpleVectorDb(vectorDbConfig, embeddingGen);
#pragma warning restore KMEXP03
            
            // Ensure the intent index exists
            await memoryDb.CreateIndexAsync("intent", vectorSize: 768); // nomic-embed-text produces 768-dim vectors
            
            foreach (var category in intentSamples.Categories)
            {
                foreach (var sub in category.Value.Subcategories)
                {
                    foreach (var question in sub.Value.Questions)
                    {
                        var docId = Helpers.HashThis(question);
                        
                        Console.WriteLine($"Uploading intent {sub.Key} with question: {question}");
                        
                        // Generate embedding for the question
                        var embedding = await embeddingGen.GenerateEmbeddingAsync(question);
                        
                        // Create a MemoryRecord directly (the format Simple Vector DB expects)
                        var record = new Microsoft.KernelMemory.MemoryStorage.MemoryRecord
                        {
                            Id = docId,
                            Vector = embedding,
                            Tags = new Microsoft.KernelMemory.TagCollection 
                            { 
                                { "intent", sub.Key }, 
                                { "mainintent", category.Key } 
                            },
                            Payload = new Dictionary<string, object>
                            {
                                { Microsoft.KernelMemory.Constants.ReservedPayloadTextField, question }
                            }
                        };
                        
                        // Upsert directly to the vector DB (bypassing the KM pipeline)
                        await memoryDb.UpsertAsync("intent", record);
                        Console.WriteLine($"- Document Id: {docId} uploaded directly to intent index");
                    }
                }
            }
        }


        /// <summary>
        /// Retrieves the value of a specific tag from a citation answer, or returns a default value if not found.
        /// </summary>
        /// <param name="answer">The citation answer object.</param>
        /// <param name="tagName">The tag name to look for.</param>
        /// <param name="defaultValue">The default value to return if the tag is not found.</param>
        /// <returns>The tag value if found, otherwise the default value.</returns>
        public static string? GetTagValue(Citation answer, string tagName, string? defaultValue = null)
        {
            if (answer == null)
            {
                return defaultValue;
            }

            if (answer.Partitions[0].Tags.ContainsKey(tagName))
            {
                return answer.Partitions[0].Tags[tagName][0];
            }


            return defaultValue;
        }

    }
}