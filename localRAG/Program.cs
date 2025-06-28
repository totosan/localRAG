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
using Spectre.Console;

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
            PrintFancyTitle();
            IEnumerable<KeyValuePair<string, string>> ENV = DotNetEnv.Env.Load(".env");
            IMPORT_PATH = Helpers.EnvVar("IMPORT_PATH") ?? throw new Exception("IMPORT_PATH not found in .env file");

            // Check if there are documents to process
            if (!Directory.Exists(IMPORT_PATH) || Directory.GetFiles(IMPORT_PATH).Length == 0)
            {
                AssistantAnswer($"No documents found in [yellow]{IMPORT_PATH}[/]. Please upload documents before starting the application.");
                return;
            }

            // Check if tags.json exists, if not, generate it
            string tagsPath = Path.Combine(Directory.GetCurrentDirectory(), "tags.json");
            if (!File.Exists(tagsPath))
            {
                AssistantAnswer("tags.json not found. Generating tags.json...");
                await GenerateTagsJsonAsync(tagsPath);
                AssistantAnswer("tags.json created.");
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
            var memoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true, debug: DEBUG_MEMORY);

            // Read existing tags for handler initialization
            Dictionary<string, Dictionary<string, List<string>>> mainTags = new();
            if (File.Exists(tagsPath))
            {
                try
                {
                    var existingTagsJson = await File.ReadAllTextAsync(tagsPath);
                    var deserializedTags = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingTagsJson);

                    if (deserializedTags != null)
                    {
                        // Convert to the required format for the handler
                        foreach (var tag in deserializedTags.Where(t => !string.IsNullOrWhiteSpace(t.Key)))
                        {
                            mainTags[tag.Key!] = new Dictionary<string, List<string>>();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error reading existing tags: {ex.Message}. Using empty tags dictionary.");
                }
            }

            // Properly register the handler with the required parameters
            memoryConnector.Orchestrator.AddHandler(new GenerateTagsHandler(
                "generate_tags",
                memoryConnector.Orchestrator,
                mainTags
            ));

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

            await ImportDocumentsAsync(kernel35, memoryConnector, promptPlugins);
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

        private static async Task ImportDocumentsAsync(Kernel kernel, MemoryServerless memoryConnector, KernelPlugin prompts)
        {
            var tags = await LongtermMemoryHelper.LoadAndStorePdfFromPathAsync(memoryConnector, IMPORT_PATH);
            if (true)
            {            // I want to have each distinct tag as a list of tags / the key itself is also a tag
                var listOfTags = new Dictionary<string, List<string>>();
                foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t.Key)))
                {
                    if (!listOfTags.ContainsKey(tag.Key!))
                    {
                        listOfTags[tag.Key!] = new List<string>();
                    }

                    if (tag.Value != null)
                    {
                        foreach (var value in tag.Value.Where(v => !string.IsNullOrWhiteSpace(v)))
                        {
                            if (!listOfTags.ContainsKey(value!))
                            {
                                listOfTags[value!] = new List<string>();
                            }
                        }
                    }
                }

                var result = await kernel.InvokeAsync<string>(prompts["IntentsPlugin"], new() { ["input"] = JsonSerializer.Serialize(listOfTags) });
                if (result != null)
                {
                    var intentListWithQuestions = result.Replace("```json\n", "").Replace("```", "").Trim();
                    var tagCollection = JsonSerializer.Deserialize<Dictionary<string, List<string?>>>(intentListWithQuestions);

                    if (tagCollection != null)
                    {
                        Console.WriteLine($"Generated {tagCollection.Count} tag collections with questions");
                    }
                }
            }
        }

        private static async Task CreateIntentionsAsync(IKernelMemory kernelMemory)
        {
            var tags = await Helpers.ReadTagsFromFile();
            await LongtermMemoryHelper.CreateIntents(kernelMemory, tags);
        }

        /// <summary>
        /// Generates the tags.json file by extracting tags and related questions from imported documents.
        /// Handles tag generation, deduplication, and incremental updates across document imports.
        /// </summary>
        /// <param name="tagsPath">The path where tags.json will be created.</param>
        private static async Task GenerateTagsJsonAsync(string tagsPath)
        {
            // Validate import path
            if (!Directory.Exists(IMPORT_PATH) || Directory.GetFiles(IMPORT_PATH).Length == 0)
            {
                Console.WriteLine($"No documents found in {IMPORT_PATH}. Cannot generate tags.");
                return;
            }

            var memoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true, debug: DEBUG_MEMORY);

            // Read or initialize tags dictionary
            var existingTags = new Dictionary<string, HashSet<string>>();
            Dictionary<string, Dictionary<string, List<string>>> mainTags = new();

            if (File.Exists(tagsPath))
            {
                try
                {
                    var existingTagsJson = await File.ReadAllTextAsync(tagsPath);
                    var deserializedTags = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(existingTagsJson);

                    if (deserializedTags != null)
                    {
                        existingTags = deserializedTags
                            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => new HashSet<string>(kvp.Value ?? Enumerable.Empty<string>())
                            );

                        // Convert to the required format for the handler
                        foreach (var tag in deserializedTags.Where(t => !string.IsNullOrWhiteSpace(t.Key)))
                        {
                            mainTags[tag.Key!] = new Dictionary<string, List<string>>();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error reading existing tags: {ex.Message}. Starting with empty tags.");
                }
            }

            // IMPORTANT: Register the handler with proper parameters BEFORE attempting to load documents
            memoryConnector.Orchestrator.AddHandler(new GenerateTagsHandler(
                "generate_tags",
                memoryConnector.Orchestrator,
                mainTags
            ));

            var kernel = Helpers.GetSemanticKernel(debug: DEBUG_KERNEL);
            var prompts = kernel.ImportPluginFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts"));

            // Now that the handler is registered, load and process the documents
            AnsiConsole.MarkupLine($"[bold blue]Loading and processing documents from:[/] [underline yellow]{IMPORT_PATH}[/]");
            var documentTags = await LongtermMemoryHelper.LoadAndStorePdfFromPathAsync(memoryConnector, IMPORT_PATH);
            AnsiConsole.MarkupLine($"[green]✔ Successfully processed documents. Found [bold]{documentTags.Count}[/] document tags.[/]");

            // Prepare tag collection, combining existing and new tags
            var combinedTags = new Dictionary<string, HashSet<string>>(existingTags);
            foreach (var docTag in documentTags.Where(dt => !string.IsNullOrWhiteSpace(dt.Key)))
            {
                // Safely add document key as a tag if not exists
                if (!combinedTags.ContainsKey(docTag.Key!))
                {
                    combinedTags[docTag.Key!] = new HashSet<string>();
                }

                // Safely add document values as tags
                if (docTag.Value != null)
                {
                    foreach (var value in docTag.Value.Where(v => !string.IsNullOrWhiteSpace(v)))
                    {
                        if (!combinedTags.ContainsKey(value!))
                        {
                            combinedTags[value!] = new HashSet<string>();
                        }
                    }
                }
            }

            // Generate questions/intents for tags
            var tagSerializationInput = combinedTags
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .ToDictionary(
                    kv => kv.Key!,
                    kv => kv.Value.ToList()
                );

            await AnsiConsole.Status()
                .StartAsync("[bold yellow]Generating questions for tags using IntentsPlugin...[/]", async ctx =>
                {
                    var result = await kernel.InvokeAsync<string>(
                        prompts["IntentsPlugin"],
                        new() { ["input"] = JsonSerializer.Serialize(tagSerializationInput) }
                    );

                    // Clean and parse generated tag questions, with null checks
                    var intentListWithQuestions = result?
                        .Replace("```json\n", "")
                        .Replace("```", "")
                        .Trim() ?? string.Empty;

                    var generatedTagCollection = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(intentListWithQuestions)
                        ?? new Dictionary<string, List<string>>();

                    // Merge and deduplicate tags
                    var mergedTags = new Dictionary<string, HashSet<string>>(existingTags);
                    foreach (var kvp in generatedTagCollection.Where(k => !string.IsNullOrWhiteSpace(k.Key)))
                    {
                        if (!mergedTags.ContainsKey(kvp.Key!))
                        {
                            mergedTags[kvp.Key!] = new HashSet<string>();
                        }

                        // Add new unique questions to existing tag questions
                        if (kvp.Value != null)
                        {
                            mergedTags[kvp.Key!].UnionWith(
                                kvp.Value
                                    .Where(q => !string.IsNullOrWhiteSpace(q))
                            );
                        }
                    }

                    // Prepare tags for serialization (convert HashSet back to List)
                    var tagsForSerialization = mergedTags
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .ToDictionary(
                            kvp => kvp.Key!,
                            kvp => kvp.Value.ToList()
                        );

                    // Write updated tags to file
                    await File.WriteAllTextAsync(
                        tagsPath,
                        JsonSerializer.Serialize(tagsForSerialization, new JsonSerializerOptions { WriteIndented = true })
                    );

                    AnsiConsole.MarkupLine($"[bold green]Tags generated and saved to[/] [underline yellow]{tagsPath}[/]. [bold]Total unique tags:[/] {tagsForSerialization.Count}");
                });
        }

        public static void DebugStep(string stepName, string message)
        {
            AnsiConsole.MarkupLine($"[bold blue][[DEBUG]][/] [bold yellow]Step:[/] [green]{stepName}[/] - {message}");
        }

        public static void PrintFancyTitle()
        {
            AnsiConsole.Write(
                new FigletText("localRAG Assistant")
                    .Centered()
                    .Color(Color.Cyan1));
            AnsiConsole.MarkupLine("[bold blue]Your modern, local Retrieval-Augmented Generation assistant[/]\n");
        }

        public static void AssistantAnswer(string message)
        {
            AnsiConsole.MarkupLine($"[bold][[ASSISTANT]][/] [springgreen3_1]{message}[/]");
        }
    }
}