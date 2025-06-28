using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.SemanticKernel;
using System.Text.Json;
using System.IO;

namespace localRAG.Process.Steps
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class LookupKernelmemoriesStep : KernelProcessStep<LookUpState>
    {
        public static class Functions
        {
            public const string RemoveIndex = nameof(RemoveIndex);
            public const string GetIntentOfAsk = nameof(GetIntentOfAsk);
            public const string GetMemoryData = nameof(GetMemoryData);
        }

        public static class OutputEvents
        {
            public static string IntentsReceived { get; set; } = nameof(IntentsReceived);
            public static string IndexesRemoved { get; set; } = nameof(IndexesRemoved);
            public static string MemoryDataReceived { get; set; } = nameof(MemoryDataReceived);

        }

        private LookUpState? _state;
        private readonly ILogger<LookupKernelmemoriesStep> _logger;

        public LookupKernelmemoriesStep(ILogger<LookupKernelmemoriesStep> logger)
        {
            _logger = logger;
        }
        public override ValueTask ActivateAsync(KernelProcessStepState<LookUpState> state)
        {
            _state = state.State;
            if (_state != null)
            {
                _state.MemoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true);

                // Create the dictionary required by GenerateTagsHandler
                Dictionary<string, Dictionary<string, List<string>>> mainTags = new();
                string tagsPath = Path.Combine(Directory.GetCurrentDirectory(), "tags.json");

                if (File.Exists(tagsPath))
                {
                    try
                    {
                        var existingTagsJson = File.ReadAllText(tagsPath);
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
                        _logger.LogError($"Error reading existing tags: {ex.Message}. Using empty tags dictionary.");
                    }
                }

                // Properly instantiate the handler with the required dictionary parameter
                _state.MemoryConnector.Orchestrator.AddHandler(new GenerateTagsHandler(
                    "generate_tags",
                    _state.MemoryConnector.Orchestrator,
                    mainTags
                ));
            }

            return ValueTask.CompletedTask;
        }

        [KernelFunction(Functions.RemoveIndex)]
        public async Task RemoveIndexAsync(KernelProcessStepContext context)
        {
            if (_state?.MemoryConnector == null)
            {
                _logger.LogError("Memory connector is not initialized");
                return;
            }

            await LongtermMemoryHelper.RemoveAllIndexsAsync(_state.MemoryConnector);
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.IndexesRemoved, Visibility = KernelProcessEventVisibility.Public });
        }

        [KernelFunction(Functions.GetIntentOfAsk)]
        public async Task AskForIntentAsync(KernelProcessStepContext context, SearchData searchData)
        {
            Console.WriteLine("[DEBUG] Step: LookupKernelmemoriesStep - AskForIntentAsync called");

            if (_state?.MemoryConnector == null)
            {
                _logger.LogError("Memory connector is not initialized");
                return;
            }

            var intents = new List<string>();
            intents.AddRange(await LongtermMemoryHelper.AskForIntentAsync(searchData.StandaloneQuestions.First().StandaloneQuestion, _state.MemoryConnector));
            searchData.Intents = intents;
            _logger.LogInformation("Intents: " + string.Join("\n#", intents));
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.IntentsReceived, Data = searchData });
        }

        [KernelFunction(Functions.GetMemoryData)]
        public async Task GetFromMemoryAsync(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            Console.WriteLine("[DEBUG] Step: LookupKernelmemoriesStep - GetFromMemoryAsync called");

            if (_state?.MemoryConnector == null)
            {
                _logger.LogError("Memory connector is not initialized");
                return;
            }

            var chatHistory = await _kernel.GetHistory().GetHistoryAsync();
            var userInput = searchData.UserMessage;
            var intents = searchData.Intents;

            //var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(_state!.MemoryConnector, searchData.StandaloneQuestions.First().StandaloneQuestion);
            var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(_state.MemoryConnector, searchData.StandaloneQuestions.First().StandaloneQuestion, intents: intents);
            _logger.LogInformation($"Long term memory:\n\t{longTermMemory}");

            chatHistory.AddUserMessage($"\n{longTermMemory}");
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.MemoryDataReceived, Data = searchData });
        }
    }

#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class LookUpState
    {
        public MemoryServerless? MemoryConnector { get; set; }
    }
}