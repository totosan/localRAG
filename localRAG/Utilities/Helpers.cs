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

      



/// <summary>
/// Get a Semantic Kernel instance with Azure OpenAI configuration.
/// If `weakGpt` is true, it uses a lower-tier model.
/// If `debug` is true, it sets the logging level to Debug.
/// If `history` is provided, it uses it as the chat history provider.
/// 
/// This method is used to create a Semantic Kernel instance that can be used for various AI tasks.
/// It configures the kernel with Azure OpenAI settings and sets up logging based on the debug flag.
/// The chat history provider is also set if a history object is provided.
/// 
/// Note: Ensure that the environment variables for Azure OpenAI are set correctly before calling this method.
/// 
/// Example usage:
/// var kernel = Helpers.GetSemanticKernel(weakGpt: false, debug: true, history: myChatHistory);
/// </summary>
/// <param name="weakGpt">Whether to use a weaker GPT model.</param>
/// <param name="debug">Whether to enable debug logging.</param>
/// <param name="history">The chat history provider.</param>
/// <returns></returns>
        public static Kernel GetSemanticKernel(bool weakGpt = false, bool debug = false, ChatHistory history = null)
        {
            Kernel kernel;
            IKernelBuilder builder;
            bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";

            if (useOllama)
            {
                Console.WriteLine($"used model (Ollama): {Environment.GetEnvironmentVariable("OLLAMA_TEXT")}");
                string endpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")!;
                // Ensure endpoint ends with /v1 for OpenAI compatibility
                if (!endpoint.EndsWith("/v1"))
                {
                    endpoint = endpoint.TrimEnd('/') + "/v1";
                }

#pragma warning disable SKEXP0010
                builder = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(
                        modelId: Environment.GetEnvironmentVariable("OLLAMA_TEXT")!,
                        apiKey: "ollama",
                        endpoint: new Uri(endpoint)
                    );
#pragma warning restore SKEXP0010
            }
            else if (!weakGpt)
            {
                Console.WriteLine($"used model for intelligent tasks: {Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL")}");
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
                Console.WriteLine($"used model for simple tasks: {Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_LOW")}");
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
                    //.WithMongoDbAtlasMemoryDbAndDocumentStorage(mongoConfig)
                    .WithSimpleFileStorage()
                    .WithSimpleTextDb()
                    .WithSimpleVectorDb()
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
                .WithSimpleFileStorage("tmp-data")
                .WithSimpleVectorDb("tmp-data")
                .WithContentDecoder<CustomPdfDecoder>()
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

        public static string EnvVarOrDefault(string name, string defaultValue)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
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
    }

}
