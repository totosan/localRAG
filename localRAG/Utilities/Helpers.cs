using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AI.Ollama;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.MongoDbAtlas;
using localRAG.Models;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using DocumentFormat.OpenXml.Math;
using Microsoft.SemanticKernel.ChatCompletion;
using localRAG.Utilities;
using System.Diagnostics;

namespace localRAG
{

    public class Helpers
    {

        public static async Task<TagCollection> LoadAndStorePdfFromPath(IKernelMemory memoryConnector, string path)
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

                //tags = await GetTagsFromDocumentById(memoryConnector, file, fileId);
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

        public static async Task CreateIntents(IKernelMemory memoryConnector, DocumentCategories intentSamples)
        {
            foreach (var category in intentSamples.Categories)
            {
                foreach (var sub in category.Value.Subcategories)
                {
                    foreach (var question in sub.Value.Questions)
                    {
                        var docId = HashThis(question);
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
        public static async Task<List<string>> AskForIntent(string request, IKernelMemory s_memory)
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
                if(retrievedMainIntents != null &&
                     intents.Find(i => i == retrievedMainIntents) == null)
                {
                    intents.Add(retrievedMainIntents);
                }   
            }

            return intents;
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

        public static async Task<DocumentCategories> ReadTagsFromFile()
        {
            IEnumerable<KeyValuePair<string, string>> ENV = DotNetEnv.Env.Load(".env");
            var tagsFile = Helpers.EnvVar("TAGS_COLLECTION_FILE") ?? throw new Exception("TAGS not found in .env file");
            var tagsFileText = await File.ReadAllTextAsync(tagsFile);
            // Deserialize the JSON into a dictionary
            var rawCategories = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(tagsFileText);

            // Create a new instance of DocumentCategories
            var documentCategories = new DocumentCategories();

            // Loop through the dictionary and populate the DocumentCategories instance
            foreach (var category in rawCategories)
            {
                var documentCategory = new DocumentCategory();
                foreach (var subcategory in category.Value)
                {
                    var documentQuestions = new DocumentQuestions { Questions = subcategory.Value };
                    documentCategory.Subcategories[subcategory.Key] = documentQuestions;
                }
                documentCategories.Categories[category.Key] = documentCategory;
            }

            return documentCategories;
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
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToUpperInvariant();
        }
        public static Kernel GetSemanticKernel(bool weakGpt = false, bool debug = false, ChatHistory history = null)
        {
            Kernel kernel;
            IKernelBuilder builder;
            if (!weakGpt)
            {
                Console.WriteLine(Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL"));
                builder = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL")!,  // The name of your deployment (e.g., "text-davinci-003")
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                )
                ;
            }
            else
            {
                Console.WriteLine(Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_LOW"));
                builder = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_LOW")!,  // The name of your deployment (e.g., "text-davinci-003")
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                )
                ;
            }
            var loggingLevel = debug ? LogLevel.Debug : LogLevel.Warning;
            builder.Services.AddLogging(loggingBuilder =>
                       {
                           loggingBuilder.AddConsole();
                           loggingBuilder.SetMinimumLevel(loggingLevel);
                       });
            builder.Services.AddSingleton<IChatHistoryProvider>(new ChatHistoryProvider(history));
            kernel = builder.Build();
            return kernel;
        }


        public static T GetMemoryConnector<T>(bool serverless = false, bool useAzure = false, bool debug = false) where T : IKernelMemory
        {
            if (!serverless)
            {
                var client = new MemoryWebClient("http://portainer.fritz.box:9001/");
                return (T)(IKernelMemory)client;
            }
            else if (useAzure)
            {
                var mongoConfig = new MongoDbAtlasConfig();
                mongoConfig.ConnectionString = EnvVar("MONGODB_CONNECTION_STRING");
                mongoConfig.DatabaseName = EnvVar("MONGODB_DATABASE_NAME");
                mongoConfig.WithSingleCollectionForVectorSearch(true);

                var kernel = new KernelMemoryBuilder()
                    .WithAzureOpenAITextEmbeddingGeneration(new AzureOpenAIConfig
                    {
                        APIType = AzureOpenAIConfig.APITypes.EmbeddingGeneration,
                        Endpoint = EnvVar("AZURE_OPENAI_ENDPOINT"),
                        Deployment = EnvVar("AOAI_DEPLOYMENT_EMBEDDING"),
                        Auth = AzureOpenAIConfig.AuthTypes.APIKey,
                        APIKey = EnvVar("AZURE_OPENAI_API_KEY"),
                    })
                    .WithAzureOpenAITextGeneration(new AzureOpenAIConfig
                    {
                        APIType = AzureOpenAIConfig.APITypes.ChatCompletion,
                        Endpoint = EnvVar("AZURE_OPENAI_ENDPOINT"),
                        Deployment = EnvVar("AOAI_DEPLOYMENT_TEXT"),
                        Auth = AzureOpenAIConfig.AuthTypes.APIKey,
                        APIKey = EnvVar("AZURE_OPENAI_API_KEY"),
                    })
                    .WithMongoDbAtlasMemoryDbAndDocumentStorage(mongoConfig)
                    //.WithSimpleFileStorage(SimpleFileStorageConfig.Persistent)
                    //.WithSimpleTextDb(SimpleTextDbConfig.Persistent)
                    //.WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
                    // use the dateformat schema from '2010-04-16T10:00:00.000Z'
                    //.With(new MsExcelDecoderConfig { DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ", DateFormatProvider = CultureInfo.InvariantCulture })
                    .WithCustomImageOcr(new TesseractOCR())
                    .WithContentDecoder<CustomPdfDecoder>();
                
                // ##### setup logging #####
                var loggingLevel = debug ? LogLevel.Debug : LogLevel.Warning;
                kernel.Services.AddLogging(loggingBuilder =>
                           {
                               loggingBuilder.AddConsole();
                               loggingBuilder.SetMinimumLevel(loggingLevel);
                           });


                return (T)(IKernelMemory)kernel.Build<MemoryServerless>();
            }
            else
            {
                var config = new OllamaConfig
                {
                    Endpoint = EnvVar("OLLAMA_ENDPOINT"),
                    TextModel = new OllamaModelConfig(EnvVar("OLLAMA_TEXT")),
                    EmbeddingModel = new OllamaModelConfig(EnvVar("OLLAMA_EMBEDDING"))

                };

                return (T)(IKernelMemory)new KernelMemoryBuilder()
                .WithOllamaTextGeneration(config, new CL100KTokenizer())
                .WithOllamaTextEmbeddingGeneration(config, new CL100KTokenizer())
                .Build();
            }
        }
        public static string HashThis(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();
        }
        public static string EnvVar(string name)
        {
            return Environment.GetEnvironmentVariable(name)
                   ?? throw new ArgumentException($"Env var {name} not set");
        }

        public static string ChatHistoryToString(ChatHistory chatHist, string? userInput)
        {
            StringBuilder userMessageBuilder = new();
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var messageCount = chatHist.Where(c => c.Role == AuthorRole.User).Count();
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            if (messageCount > 9)
            {
                userMessageBuilder.Append(chatHist[0].Content);
                userMessageBuilder.Append("\n");
                userMessageBuilder.Append(chatHist.TakeLast(5).Aggregate("", (acc, item) => acc + "\n" + item.Content));
                userMessageBuilder.Append("\n");
                userMessageBuilder.Append(userInput);
            }
            else
            {
                userMessageBuilder.Append(chatHist.Aggregate("", (acc, item) => acc + "\n" + item.Content));
                userMessageBuilder.Append("\n");
                userMessageBuilder.Append(userInput);
            }
            userInput = userMessageBuilder.ToString();
            return userInput;
        }
    }

}
