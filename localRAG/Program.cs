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
using MongoDB.Driver.Linq;
using Microsoft.VisualBasic;
using System.Collections;

namespace localRAG
{
    partial class Program
    {
        private static string IMPORT_PATH = "";
        private const string SYSTEM_PROMPT = """
                           You are a helpful assistant replying to user questions using information from your memory. You use the concept of RAG (Retreival augmented generation), to work with local documents.
                           Reply very briefly and concisely, get to the point immediately. Don't provide long explanations unless necessary.
                           Sometimes you don't have relevant memories so you reply saying you don't know, don't have the information.
                           For retrieving information to answer complex questions, you have to first plan your search strategy by deciding which steps to take.
                           Please first come up with a plan and then execute it. You can ask for help if you are stuck.
                           Always respond with the source' name and partition nr in Format [DocName:PartitionNr] if you provide information from a document.
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


            // =================================================
            // === PREPARE SEMANTIC FUNCTION USING DEFAULT INDEX
            // =================================================

            Kernel kernel = Helpers.GetSemanticKernel();
            Kernel kernel35 = Helpers.GetSemanticKernel(true);

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
            //var memoryIndexAsk = kernel.CreateFunctionFromPrompt(SK_PROMPT_ASK, promptOptions);
            //var memoryIndexSearch = kernel.CreateFunctionFromPrompt(SK_PROMPT_SEARCH, promptOptions);

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
            var promptPlugins = kernel35.ImportPluginFromPromptDirectory(path);
            var intentPrompt = promptPlugins["IntentsPlugin"];
            var haluCheckPrompt = promptPlugins["HalucinationCheckPlugin"];
            var ragOrNotRagPrompt = promptPlugins["RagOrNotRag"];
            var rewriteUserAskPrompt = promptPlugins["RewriteUserAskPlugin"];

            // ==================================
            // === LOAD DOCUMENTS INTO MEMORY ===
            // ==================================


            await ImportDocuments(kernel35, memoryConnector, promptPlugins);


            // ==============================================
            // ===                RUN THE CHAT            ===
            // ==============================================


            // add a user input loop for interactive testing
            //chatHistory.AddAssistantMessage("Hello, I'm your assistant. Ask me anything about your own documents.");
            var reply = new StringBuilder();
            var chathistoryservice = kernel.GetRequiredService<IChatCompletionService>();

            var longtermDone = false;
            var messageCount = 0;
            while (true)
            {
                Console.WriteLine("---------");
                Console.WriteLine("Enter a question or type 'exit' to quit:");
                var userInput = Console.ReadLine()?.Trim();

                var clearMessages = () =>
                {
                    chatHistory.Clear();
                    chatHistory.AddSystemMessage(systemPrompt);
                    messageCount = 0;
                };

                if (string.IsNullOrWhiteSpace(userInput))
                { continue; }
                else
                {
                    if (userInput.StartsWith("/"))
                    {
                        switch (userInput.ToLower())
                        {
                            case "/q":
                            case "/exit":
                                return;
                            case "/clear":
                                clearMessages();
                                continue;
                            case "/ri":
                            case "/removeindex":
                                await Helpers.RemoveAllIndexs(memoryConnector);
                                clearMessages();
                                continue;
                            case "/reimport":
                            case "/im":
                                await ImportDocuments(kernel35, memoryConnector, promptPlugins);
                                clearMessages();
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
                    messageCount++;
                }

                // in case of long history, reduce the input to the last 5 messages
                // compose the working batch as a single string
                // SystemPrompt + 5 last messages + userMessage
                if (false)
                {
                    var orig_userInput = userInput;
                    if (messageCount > 4)
                    {
                        var lastDialogItems = chatHistory.TakeLast(5).Aggregate("", (acc, item) => acc + "\n" + item.Content);
                        lastDialogItems = chatHistory[0].Content + lastDialogItems;
                        userInput = lastDialogItems.Split("\n").Aggregate("", (acc, item) => acc + '\n' + item);
                        userInput += "\n" + orig_userInput;
                    }
                    else
                    {
                        userInput = chatHistory.Aggregate("", (acc, item) => acc + "\n" + item.Content);
                        userInput += "\n" + orig_userInput;
                    }
                }

                // ----------- REWRITE LAST USER ASK ----------------
                var userInputs = await RewriteUserAsk(logger, kernel35, rewriteUserAskPrompt, messageCount, chatHistory, userInput);

                // ----------- ROUTING TO RAG OR NOT RAG ----------------
                var rag_search = await Router(logger, kernel, ragOrNotRagPrompt, messageCount, chatHistory, userInputs.Last());

                if (!rag_search) // just using data from chat history
                {
                    chatHistory.AddUserMessage(userInput);
                }
                else
                {
                    // --- GET INTENTS ---
                    var intents = new List<string>();
                    foreach (var input in userInputs)
                    {
                        intents.AddRange(await Helpers.AskForIntent(input, memoryConnector));
                    }
                    logger.LogInformation("Intents: " + string.Join("\n#", intents));

                    // --- GET LONG TERM MEMORY ---
                    var longTermMemory = await Helpers.GetLongTermMemory(memoryConnector, userInput, intents: intents);
                    logger.LogInformation($"Long term memory:\n\t{longTermMemory}");

                    chatHistory.AddUserMessage($"\n{longTermMemory}\n{userInput}");
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

        private static async Task<List<string?>> RewriteUserAsk(ILogger logger, Kernel kernel, KernelFunction rewriteUserAskPrompt, int messageCount, ChatHistory chatHist, string? userInput)
        {
            List<UserAsk> rewrittenQuestions = new();
            var messages = ChatHistoryToString(messageCount, chatHist, userInput);
            try
            {
                var userask = await kernel.InvokeAsync<string>(rewriteUserAskPrompt, new() { ["question"] = messages });
                userask = userask.Replace("```json\n", "").Replace("```", "").Trim();
                logger.LogInformation("Rewritten user ask: " + userask);

                rewrittenQuestions = JsonSerializer.Deserialize<List<UserAsk>>(userask);
            }
            catch (System.Exception e)
            {
                logger.LogError("Error in rewriting user ask: " + e.Message);
                logger.LogError($"\tUser ask: \n\t{rewrittenQuestions.Aggregate("", (acc, item) => acc + "\n" + item.StandaloneQuestion)}");
            }
            return rewrittenQuestions.Select(x => x.StandaloneQuestion).ToList();
        }

        private static async Task<bool> Router(ILogger logger, Kernel kernel, KernelFunction ragOrNotRagPrompt, int messageCount, ChatHistory chatHist, string? userInput)
        {
            bool rag_search;
            if (messageCount == 1) // the first user message always goes to RAG
            {
                rag_search = true;
            }
            else
            {
                var message = ChatHistoryToString(messageCount, chatHist, userInput);

                // invoke plugin to decide if the message should go to RAG or not
                var ragAsk = await kernel.InvokeAsync<string>(ragOrNotRagPrompt, new() { ["chat"] = message });

                rag_search = bool.Parse(ragAsk.Replace("```json\n", "").Replace("```", "").Trim());
                logger.LogInformation("Rag search: " + (rag_search ? "yes" : "no"));
            }

            return rag_search;
        }

        private static string ChatHistoryToString(int messageCount, ChatHistory chatHist, string? userInput)
        {
            StringBuilder userMessageBuilder = new();
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