using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.SemanticKernel;

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
            _state.MemoryConnector = Helpers.GetMemoryConnector<MemoryServerless>(serverless: true, useAzure: true);
            _state.MemoryConnector.Orchestrator.AddHandler<GenerateTagsHandler>("generate_tags"); // this adds tags according to its content

            return ValueTask.CompletedTask;
        }

        [KernelFunction(Functions.RemoveIndex)]
        public async Task RemoveIndexAsync(KernelProcessStepContext context)
        {
            await LongtermMemoryHelper.RemoveAllIndexsAsync(_state!.MemoryConnector);
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.IndexesRemoved , Visibility = KernelProcessEventVisibility.Public });
        }

        [KernelFunction(Functions.GetIntentOfAsk)]
        public async Task AskForIntentAsync(KernelProcessStepContext context, SearchData searchData)
        {
            var intents = new List<string>();
            intents.AddRange(await LongtermMemoryHelper.AskForIntentAsync(searchData.StandaloneQuestions.First().StandaloneQuestion, _state!.MemoryConnector));
            searchData.Intents = intents;
            _logger.LogInformation("Intents: " + string.Join("\n#", intents));
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.IntentsReceived, Data = searchData });
        }

        [KernelFunction(Functions.GetMemoryData)]
        public async Task GetFromMemory(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            var chatHistory = await _kernel.GetHistory().GetHistoryAsync();
            var userInput = searchData.UserMessage;
            var intents = searchData.Intents;

            //var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(_state!.MemoryConnector, searchData.StandaloneQuestions.First().StandaloneQuestion);
            var longTermMemory = await LongtermMemoryHelper.GetLongTermMemory(_state!.MemoryConnector, searchData.StandaloneQuestions.First().StandaloneQuestion, intents: intents);
            _logger.LogInformation($"Long term memory:\n\t{longTermMemory}");

            chatHistory.AddUserMessage($"\n{longTermMemory}");
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.MemoryDataReceived, Data = searchData });
        }
    }

#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class LookUpState
    {
        public MemoryServerless MemoryConnector { get; set; }
    }
}