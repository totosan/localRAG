using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using localRAG.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Context;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Utilities
{
    public class LongtermMemoryHelper
    {
        public static async Task<TagCollection> LoadAndStorePdfFromPathAsync(IKernelMemory memoryConnector, string path)
        {
            // read all pdf, docx, images and pptx n a directory
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".pdf") || file.EndsWith(".docx") || file.EndsWith(".pptx") || file.EndsWith(".jpg") || file.EndsWith(".jpeg") || file.EndsWith(".png"))
                .ToList();
            var context = new RequestContext();
            var tags = new TagCollection();

            foreach (var file in files)
            {
                var fileId = Helpers.HashThis(file);
                var saved = await memoryConnector.IsDocumentReadyAsync(fileId);
                if (!saved)
                {
                    try
                    {
                        var docid = await memoryConnector.ImportDocumentAsync(new Document(fileId)
                                                                        .AddFile(file),
                                                                steps: [Constants.PipelineStepsExtract,
                                                                            "generate_tags",
                                                                        Constants.PipelineStepsPartition,
                                                                        Constants.PipelineStepsGenEmbeddings,
                                                                        Constants.PipelineStepsSaveRecords,
                                                                            //"manage_tags"
                                                                        ],
                                                                    context: context);
                        Console.WriteLine($"\nDocument {file} is being processed\n");
                    }
                    catch (System.Exception e)
                    {
                        Console.WriteLine($"Error processing file {file}");
                        throw;
                    }

                }

                tags = await GetTagsFromDocumentById(memoryConnector, file, fileId);
                Console.WriteLine($"File: {file}\n\t- Tags: {tags}");
            }
            return tags;
        }


        private static async Task<TagCollection> GetTagsFromDocumentById(IKernelMemory memoryConnector, string? file, string fileId)
        {
            TagCollection tags = new TagCollection();
            var pipeline = await memoryConnector.GetDocumentStatusAsync(fileId);
            Console.WriteLine($"Document {file} is being processed");
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
        /// <param name="request"></param>
        /// <param name="s_memory"></param>
        /// <returns></returns>
        public static async Task<List<string>> AskForIntentAsync(string request, IKernelMemory s_memory)
        {
            SearchResult answer = await s_memory.SearchAsync(request, index: "intent", minRelevance: 0.70, limit: 3);
            List<string> intents = new List<string>();
            foreach (Citation result in answer.Results)
            {
                var retrievedIntents = GetTagValue(result, "intent", "none");
                var retrievedMainIntents = GetTagValue(result, "mainintent", "none");
                if (false && retrievedIntents != null &&
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
        /// Retrieves long-term memory based on the provided query.
        /// </summary>
        /// <param name="memory">The kernel memory interface to interact with.</param>
        /// <param name="query">The query string to search for in the memory.</param>
        /// <param name="asChunks">A boolean indicating whether to fetch the memory as chunks or not. Default is true.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the retrieved memory as a string.</returns>
        public static async Task<string> GetLongTermMemory(IKernelMemory memory, string query, bool asChunks = true, List<string> intents = null)
        {

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
                if (intents != null)
                {
                    foreach (var intent in intents)
                    {
                        filters.Add(MemoryFilters.ByTag("intent", intent));
                    }
                    memories = await memory.SearchAsync(query, minRelevance: 0.4, limit: 3, filters: filters, context: context);
                }
                else
                {
                    memories = await memory.SearchAsync(query, minRelevance: 0.4, limit: 3, context: context);
                }
                //List<SortedDictionary<int, Citation.Partition>> partCollection = await GetAdjacentChunks(memory, memories);
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
                                PartitionNumber = partition.PartitionNumber,
                                Content = partition.Text,
                                Score = partition.Relevance
                            };
                            documents.Add(doc);
                        }
                    }

                    //return partCollection.SelectMany(p => p.Values).Aggregate("", (sum, chunk) => sum + chunk.Text + "\n").Trim();
                    //return memories.Results.SelectMany(m => m.Partitions).Aggregate("", (sum, chunk) => sum + chunk.Text + "\n").Trim();
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
                        PartitionNumber = part.PartitionNumber,
                        Content = part.Text,
                        Score = part.Relevance
                    };
                    documents.Add(docsimple);
                }
            }
            return JsonSerializer.Serialize(documents);
        }


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
        public static string GetUrlId(string url)
        {
            return Helpers.HashThis(url);
        }


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


        public static async Task CreateIntents(IKernelMemory memoryConnector, DocumentCategories intentSamples)
        {
            foreach (var category in intentSamples.Categories)
            {
                foreach (var sub in category.Value.Subcategories)
                {
                    foreach (var question in sub.Value.Questions)
                    {
                        var docId = Helpers.HashThis(question);
                        if (await memoryConnector.IsDocumentReadyAsync(docId))
                        {
                            return;
                        }

                        Console.WriteLine($"Uploading intent {sub.Key} with question: {question}");
                        await memoryConnector.ImportTextAsync(question, tags: new TagCollection() { { "intent", sub.Key }, { "mainintent", category.Key } }, documentId: docId, index: "intent");
                        Console.WriteLine($"- Document Id: {docId}");
                    }
                }
            }
        }


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