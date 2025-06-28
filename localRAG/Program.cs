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
using localRAG.Process.Steps;
using localRAG.Process.StepEvents;
using Microsoft.SemanticKernel.Process.Tools;
using localRAG.Utilities;
using localRAG.Process;

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

        static bool DEBUG_KERNEL35 = false;
        static bool DEBUG_KERNEL = false;
        static bool DEBUG_MEMORY = false;

        public static async Task Main(string[] args)
        {
            IEnumerable<KeyValuePair<string, string>> ENV = DotNetEnv.Env.Load(".env");
            IMPORT_PATH = Helpers.EnvVar("IMPORT_PATH") ?? throw new Exception("IMPORT_PATH not found in .env file");

            // Check if there are documents to process
            if (!Directory.Exists(IMPORT_PATH) || Directory.GetFiles(IMPORT_PATH).Length == 0)
            {
                Console.WriteLine($"No documents found in {IMPORT_PATH}. Please upload documents before starting the application.");
                return;
            }

            // Check if tags.json exists, if not, generate it
            string tagsPath = Path.Combine(Directory.GetCurrentDirectory(), "tags.json");
            if (!File.Exists(tagsPath))
            {
                Console.WriteLine("tags.json not found. Generating tags.json...");
                await GenerateTagsJson(tagsPath);
                Console.WriteLine("tags.json created.");
            }

            // ==================================
            // ===          SETUP LOGGING     ===
            // ==================================
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Information);
            });

            ILogger logger = loggerFactory.CreateLogger<Program>();


            // =================================================
            // === PREPARE SEMANTIC FUNCTION USING DEFAULT INDEX
            // =================================================
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(SYSTEM_PROMPT);

            Kernel kernel = Helpers.GetSemanticKernel(debug: DEBUG_KERNEL, history: chatHistory); // Create a Semantic Kernel instance with the chat history. 
            Kernel kernel35 = Helpers.GetSemanticKernel(weakGpt: true, debug: DEBUG_KERNEL35, history: chatHistory);

            var promptOptions = new AzureOpenAIPromptExecutionSettings
            {
                ChatSystemPrompt = "Answer or say \"I don't know\".",
                MaxTokens = 500,
                Temperature = 00.2,
                TopP = 0,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            // ==================================
            // === PREPARE MEMORY CONNECTOR =====
            // ==================================

            // Load the Kernel Memory plugin into Semantic Kernel.
            var memoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true, debug:DEBUG_MEMORY);
            memoryConnector.Orchestrator.AddHandler<GenerateTagsHandler>("generate_tags"); // this adds tags according to its content
            //memoryConnector.Orchestrator.AddHandler<ManageTagHandler>("manage_tags"); 

            // =======================
            // === PREPARE PLUGINS ===
            // =======================


            kernel.ImportPluginFromObject(new MemoryPlugin(memoryConnector, waitForIngestionToComplete: true), "memory");
            kernel.ImportPluginFromObject(new DateTimePlugin(), "datetime");

            // create sk prompt for checking an AI chatresult for hallucinations

            var path = Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts");
            var promptPlugins_35 = kernel35.ImportPluginFromPromptDirectory(path);
            var promptPlugins = kernel.ImportPluginFromPromptDirectory(path);


            // ==================================
            // === LOAD DOCUMENTS INTO MEMORY ===
            // ==================================

            await ImportDocuments(kernel35, memoryConnector, promptPlugins);
            //await CreateIntentionsAsync(memoryConnector);

            // ==================================
            // === Create Process WF for RAG ===
            // ==================================

            // Process definition
            // step events:
            // - Start Process
            // - Get users chat input
            // - Get response to user
            // (sub process)
            //      - Rewrite users ask
            //      - Get route (search is RAG or not)
            //      - Get intent of ask
            // - Get result from RAG
            // - Get result from chat history

#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var mainProcess = new ProcessBuilder("RAG");
            var chatUserInputStep = mainProcess.AddStepFromType<ChatUserInputStep>();
            var searchRAGStep_sub = mainProcess.AddStepFromProcess(SearchProcess.CreateProcess());
            var DatasourceMaintenanceStep = mainProcess.AddStepFromType<DatasourceMaintenanceStep>();

            mainProcess
                .OnInputEvent(CommonEvents.StartProcessSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(chatUserInputStep, ChatUserInputStep.Functions.GetUserInput));
            chatUserInputStep
                .OnEvent(CommonEvents.ExitSend)
                .StopProcess();
            chatUserInputStep
                .OnEvent(ChatUserInputStep.OutputEvents.UsersChatInputReceived)
                .SendEventTo(searchRAGStep_sub.WhereInputEventIs(RewriteAskStep.OutputEvents.RewriteUsersAskSend));
            chatUserInputStep
                .OnEvent(ChatUserInputStep.OutputEvents.ChatLoopSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(chatUserInputStep, ChatUserInputStep.Functions.GetUserInput));
            chatUserInputStep
                .OnEvent(ChatUserInputStep.OutputEvents.RemoveIndexSend)
                .SendEventTo(searchRAGStep_sub.WhereInputEventIs(ChatUserInputStep.OutputEvents.ReimportDocumentsSend));
            searchRAGStep_sub
                .OnEvent(CommonEvents.ResponseToUserSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(chatUserInputStep, ChatUserInputStep.Functions.GetUserInput));
            searchRAGStep_sub
                .OnEvent(LookupKernelmemoriesStep.OutputEvents.IndexesRemoved)
                .SendEventTo(new ProcessFunctionTargetBuilder(chatUserInputStep));
            
            var process = mainProcess.Build();

            if (false)
            {
                // Generate a Mermaid diagram for the process and print it to the console
                string mermaidGraph = process.ToMermaid();
                Console.WriteLine($"=== Start - Mermaid Diagram for '{mainProcess.Name}' ===");
                Console.WriteLine(mermaidGraph);
                Console.WriteLine($"=== End - Mermaid Diagram for '{mainProcess.Name}' ===");

                // Generate an image from the Mermaid diagram
                //string generatedImagePath = await MermaidRenderer.GenerateMermaidImageAsync(mermaidGraph,"ChatBotProcess.png");
                //Console.WriteLine($"Diagram generated at: {generatedImagePath}");
            }

            using var runningProcess = await process.StartAsync(kernel, new KernelProcessEvent { Id = CommonEvents.StartProcessSend });

            return;
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        }

        private static async Task ImportDocuments(Kernel kernel, MemoryServerless memoryConnector, KernelPlugin prompts)
        {
            var tags = await LongtermMemoryHelper.LoadAndStorePdfFromPathAsync(memoryConnector, IMPORT_PATH);
            if (true)
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
    
        private static async Task CreateIntentionsAsync(IKernelMemory kernelMemory)
        {
           var tags = await Helpers.ReadTagsFromFile();
           await LongtermMemoryHelper.CreateIntents(kernelMemory , tags);
        }

        /// <summary>
        /// Generates the tags.json file by extracting tags and related questions from imported documents.
        /// Avoids duplicate questions within each tag.
        /// </summary>
        /// <param name="tagsPath">The path where tags.json will be created.</param>
        private static async Task GenerateTagsJson(string tagsPath)
        {
            var memoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true, debug: DEBUG_MEMORY);
            var kernel = Helpers.GetSemanticKernel(debug: DEBUG_KERNEL);
            var prompts = kernel.ImportPluginFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts"));
            var tags = await LongtermMemoryHelper.LoadAndStorePdfFromPathAsync(memoryConnector, IMPORT_PATH);
            var listOfTags = new Dictionary<string, HashSet<string>>();
            foreach (var tag in tags)
            {
                if (!listOfTags.ContainsKey(tag.Key))
                {
                    listOfTags[tag.Key] = new HashSet<string>();
                }
                foreach (var value in tag.Value)
                {
                    if (!listOfTags.ContainsKey(value))
                    {
                        listOfTags[value] = new HashSet<string>();
                    }
                }
            }
            // Generate questions/intents for tags
            var result = await kernel.InvokeAsync<string>(prompts["IntentsPlugin"], new() { ["input"] = JsonSerializer.Serialize(listOfTags.ToDictionary(kv => kv.Key, kv => kv.Value.ToList())) });
            var intentListWithQuestions = result.Replace("```json\n", "").Replace("```", "").Trim();
            // Parse and deduplicate questions within each tag
            var tagCollection = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(intentListWithQuestions);
            var deduped = new Dictionary<string, List<string>>();
            foreach (var kv in tagCollection)
            {
                deduped[kv.Key] = new List<string>(new HashSet<string>(kv.Value));
            }
            await File.WriteAllTextAsync(tagsPath, JsonSerializer.Serialize(deduped, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}