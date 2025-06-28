using System.Runtime.CompilerServices;
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
        /// <summary>
        /// Loads and stores PDF, DOCX, PPTX, and image files from the specified directory into memory, extracting tags for each document.
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        /// <param name="path">The directory path to search for files.</param>
        /// <returns>A TagCollection containing tags from the last processed file.</returns>
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
                                                                        steps: [
                                                                            Constants.PipelineStepsExtract,
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
        /// <param name="request">The user request string.</param>
        /// <param name="s_memory">The kernel memory connector instance.</param>
        /// <returns>A list of intent strings found in memory.</returns>
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
        /// Retrieves long-term memory based on the provided query, optionally using intent filters and chunking.
        /// </summary>
        /// <param name="memory">The kernel memory interface to interact with.</param>
        /// <param name="query">The query string to search for in the memory.</param>
        /// <param name="asChunks">Whether to fetch the memory as chunks. Default is true.</param>
        /// <param name="intents">Optional list of intent filters.</param>
        /// <returns>A JSON string containing the retrieved memory as a list of DocumentsSimple objects.</returns>
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
                                PartitionNumber = partition.PartitionNumber,
                                Content = partition.Text,
                                Score = float.IsNaN(partition.Relevance) || float.IsInfinity(partition.Relevance) ? 0 : partition.Relevance
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
        /// </summary>
        /// <param name="memoryConnector">The kernel memory connector instance.</param>
        /// <param name="intentSamples">The intent samples containing categories and questions.</param>
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