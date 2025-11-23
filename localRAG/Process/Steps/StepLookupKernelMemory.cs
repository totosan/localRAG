using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.SemanticKernel;
using System.Text.Json;
using System.IO;
using System.Linq;

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
                bool useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA")?.ToLower() == "true";
                _state.MemoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: !useOllama);

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
            if (_state?.MemoryConnector == null)
            {
                _logger.LogError("Memory connector is not initialized");
                return;
            }

            var standaloneQuestion = searchData.StandaloneQuestions.First().StandaloneQuestion;

            var intents = new List<string>();
            intents.AddRange(await LongtermMemoryHelper.AskForIntentAsync(standaloneQuestion, _state.MemoryConnector));
            searchData.Intents = intents;
            _logger.LogInformation("Intents: " + string.Join("\n#", intents));
            TraceLogger.Log($"[LookupKernelmemoriesStep] Detected intents: {string.Join(", ", intents)}");

            // Extract keyword filters from the user ask for hybrid routing
            var keywordFilters = KeywordExtractor
                .ExtractKeywords(standaloneQuestion, maxKeywords: 6)
                .Concat(KeywordExtractor.ExtractNamedEntities(standaloneQuestion, maxEntities: 4))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            searchData.KeywordFilters = keywordFilters;
            if (keywordFilters.Count > 0)
            {
                _logger.LogInformation("Keyword filters: " + string.Join(", ", keywordFilters));
                TraceLogger.Log($"[LookupKernelmemoriesStep] Extracted keywords: {string.Join(", ", keywordFilters)}");
            }
            else
            {
                _logger.LogInformation("Keyword filters: none extracted");
                TraceLogger.Log("[LookupKernelmemoriesStep] No keywords extracted");
            }

            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.IntentsReceived, Data = searchData });
        }

        [KernelFunction(Functions.GetMemoryData)]
        public async Task GetFromMemoryAsync(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            if (_state?.MemoryConnector == null)
            {
                _logger.LogError("Memory connector is not initialized");
                return;
            }

            var chatHistory = await _kernel.GetHistory().GetHistoryAsync();
            var userInput = searchData.UserMessage;
            var intents = searchData.Intents;
            var keywordFilters = searchData.KeywordFilters;

            //var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(_state!.MemoryConnector, searchData.StandaloneQuestions.First().StandaloneQuestion);
            var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(
                _state.MemoryConnector, 
                searchData.StandaloneQuestions.First().StandaloneQuestion, 
                intents: intents ?? new List<string>(), 
                keywordFilters: keywordFilters ?? new List<string>());
            
            _logger.LogInformation($"Long term memory:\n\t{longTermMemory}");

            chatHistory.AddUserMessage($"Context:\n{longTermMemory}\n\nPlease answer the question using the context above. When citing sources, include the FilePath and PartitionPath.");
            searchData.RagPerformed = true;  // Mark that RAG was performed
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.MemoryDataReceived, Data = searchData });
        }
    }

#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class LookUpState
    {
        public MemoryServerless? MemoryConnector { get; set; }
    }
}