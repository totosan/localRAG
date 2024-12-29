using Microsoft.SemanticKernel;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers.TT;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using localRAG.Plugins;
using System;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Diagnostics;
using Microsoft.KernelMemory.Handlers;
using System.Text.Json;
using Azure.Search.Documents;

namespace localRAG
{
    partial class Program
    {
        private static string IMPORT_PATH = "";
        private const string SYSTEM_PROMPT = """
                           You are a helpful assistant replying to user questions using information from your memory.
                           Reply very briefly and concisely, get to the point immediately. Don't provide long explanations unless necessary.
                           Sometimes you don't have relevant memories so you reply saying you don't know, don't have the information.
                           The topic of the conversation is Kernel Memory (KM) and Semantic Kernel (SK).
                           """;
        private const string SK_PROMPT_ASK = """
                       Question: {{$input}}
                       Tool call result: {{memory.ask $input}}
                       If the answer is empty say "I don't know", otherwise reply with a preview of the answer, truncated to 15 words.
                       """;
        private const string SK_PROMPT_SEARCH = """
                   Question: {{$input}}
                   Tool call result: {{memory.search $input}}
                   If the answer is empty say "I don't know", otherwise reply with a preview of the answer, truncated to 15 words.
                   """;

        public static async Task Main(string[] args)
        {
            IEnumerable<KeyValuePair<string, string>> ENV = DotNetEnv.Env.Load(".env");
            IMPORT_PATH = Helpers.EnvVar("IMPORT_PATH") ?? throw new Exception("IMPORT_PATH not found in .env file");

            // ==================================
            // ===          SETUP LOGGING     ===
            // ==================================
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Debug);
            });

            ILogger logger = loggerFactory.CreateLogger<Program>();

            // Example usage of logger
            logger.LogDebug("This is a debug message");

            // =================================================
            // === PREPARE SEMANTIC FUNCTION USING DEFAULT INDEX
            // =================================================

            Kernel kernel = Helpers.GetSemanticKernel();

            var chatHistory = new ChatHistory();
            var systemPrompt = SYSTEM_PROMPT;

            chatHistory.AddSystemMessage(systemPrompt);

            var promptOptions = new AzureOpenAIPromptExecutionSettings
            {
                ChatSystemPrompt = "Answer or say \"I don't know\".",
                MaxTokens = 500,
                Temperature = 00.2,
                TopP = 0,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
            var memoryIndexAsk = kernel.CreateFunctionFromPrompt(SK_PROMPT_ASK, promptOptions);

            var memoryIndexSearch = kernel.CreateFunctionFromPrompt(SK_PROMPT_SEARCH, promptOptions);

            // ==================================
            // === PREPARE MEMORY CONNECTOR =====
            // ==================================

            // Load the Kernel Memory plugin into Semantic Kernel.
            var memoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true);
            memoryConnector.Orchestrator.AddHandler<GenerateTagsHandler>("generate_tags"); // this adds tags according to its content
            //memoryConnector.Orchestrator.AddHandler<ManageTagHandler>("manage_tags"); 


            // =======================
            // === PREPARE PLUGINS ===
            // =======================


            kernel.ImportPluginFromObject(new MemoryPlugin(memoryConnector, waitForIngestionToComplete: true), "memory");
            kernel.ImportPluginFromObject(new DateTimePlugin(), "datetime");

            // create sk prompt for checking an AI chatresult for hallucinations

            var path = Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts");
            var prompts = kernel.ImportPluginFromPromptDirectory(path, "IntentsPlugin");

            // ==================================
            // === LOAD DOCUMENTS INTO MEMORY ===
            // ==================================


            await ImportDocuments(kernel, memoryConnector, prompts);


            // ==============================================
            // ===                RUN THE CHAT            ===
            // ==============================================


            // add a user input loop for interactive testing
            chatHistory.AddAssistantMessage("Hello, I'm your assistant. Ask me anything about documents stored in kernel memories.");
            var reply = new StringBuilder();
            var chathistoryservice = kernel.GetRequiredService<IChatCompletionService>();

            var longtermDone = false;
            while (true)
            {
                Console.WriteLine("---------");
                Console.WriteLine("Enter a question or type 'exit' to quit:");
                var userMessage = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(userMessage))
                { continue; }
                else
                {
                    if (userMessage.StartsWith("/"))
                    {
                        switch (userMessage.ToLower())
                        {
                            case "/q":
                            case "/exit":
                                return;
                            case "/clear":
                                chatHistory.Clear();
                                chatHistory.AddSystemMessage(systemPrompt);
                                continue;
                            case "/ri":
                            case "/removeindex":
                                await Helpers.RemoveAllIndexs(memoryConnector);
                                continue;
                            case "/reimport":
                            case "/im":
                                await ImportDocuments(kernel, memoryConnector, prompts);
                                continue;
                            case "/gi":
                            case "/GenerateIntents":
                                var tags = await Helpers.ReadTagsFromFile();

                                await Helpers.CreateIntents(memoryConnector, tags);
                                continue;
                            case "/h":
                            case "/help":
                                Console.WriteLine("Commands:");
                                Console.WriteLine("\t/exit - Exit the program");
                                Console.WriteLine("\t/clear - Clear the chat history");
                                Console.WriteLine("\t/removeIndex - Delete all indexes");
                                Console.WriteLine("\t/reimport - Reimport all documents");
                                continue;
                            default:
                                Console.WriteLine("Unknown command. Type /help for a list of commands.");
                                continue;
                        }
                    }
                    chatHistory.AddUserMessage(userMessage);
                }

                //var intent = Helpers.AskForIntent(userMessage, memoryConnector);
                if (true)
                {
                    var intents = await Helpers.AskForIntent(userMessage, memoryConnector);

                    // === ASK MEMORY ===================
                    // Recall relevant information from memory
                    // ==================================
                    var longTermMemory = await Helpers.GetLongTermMemory(memoryConnector, userMessage, intents: intents);
                    //Console.WriteLine($"-------------------------- recall from memory\n{longTermMemory}\n--------------------------");

                    // Inject the memory recall in the initial system message
                    chatHistory[0].Content = $"{systemPrompt}\n\nLong term memory:\n{longTermMemory}";
                    longtermDone = true;
                }
                // Generate the next chat message, stream the response
                Console.Write("\nCopilot> ");
                reply.Clear();
                await foreach (StreamingChatMessageContent stream in chathistoryservice.GetStreamingChatMessageContentsAsync(chatHistory, promptOptions, kernel))
                {
                    Console.Write(stream.Content);
                    reply.Append(stream.Content);
                }
                // serialize the chat history to json
                var chathistoryJson = JsonSerializer.Serialize(chatHistory);

                // check the reply for hallucinations
                //var result = await kernel.InvokeAsync(haluCheck["halucinationPlugin"], new() { ["question"] = chathistoryJson, ["answer"] = reply.ToString() });
                //Console.WriteLine($"Halucination check result: {result}");

                chatHistory.AddAssistantMessage(reply.ToString());
                Console.WriteLine("\n");

            }

        }

        private static async Task ImportDocuments(Kernel kernel, MemoryServerless memoryConnector, KernelPlugin prompts)
        {
            var tags = await Helpers.LoadAndStorePdfFromPath(memoryConnector, IMPORT_PATH);
            if (false)
            {            // I want to have each distinct tag as a list of tags / the key itself is also a tag
                var listOfTags = new Dictionary<string, List<string>>();
                foreach (var tag in tags)
                {
                    if (!listOfTags.ContainsKey(tag.Key))
                    {
                        listOfTags[tag.Key] = new List<string>();
                    }
                    foreach (var value in tag.Value)
                    {
                        if (!listOfTags.ContainsKey(value))
                        {
                            listOfTags[value] = new List<string>();
                        }
                    }
                }
                var result = await kernel.InvokeAsync<string>(prompts["IntentsPlugin"], new() { ["input"] = JsonSerializer.Serialize(listOfTags) });
                var intentListWithQuestions = result.Replace("```json\n", "").Replace("```", "").Trim();
                var tagCollection = JsonSerializer.Deserialize<Dictionary<string, List<string?>>>(intentListWithQuestions);
            }
        }
    }
}