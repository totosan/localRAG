using Elastic.Clients.Elasticsearch;
using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Process.Steps
{

#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class RoutingStep : KernelProcessStep
    {
        private readonly ILogger<RoutingStep> logger;

        public RoutingStep(ILogger<RoutingStep> logger)
        {
            this.logger = logger;
        }

        public static class Functions
        {
            public const string RoutingStep = nameof(RoutingStep);
        }
        public static class OutputEvents
        {
            public static string NoRagSearchNeeded { get; internal set; } = nameof(NoRagSearchNeeded);
            public static string RagSearchRequested { get; internal set; } = nameof(RagSearchRequested);
        }


        [KernelFunction(Functions.RoutingStep)]
        public async Task RouteAsync(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            Console.WriteLine("[DEBUG] Step: RoutingStep - RouteAsync called");
            var historyProvider = _kernel.GetHistory();
            var history = await historyProvider.GetHistoryAsync();
            var kernel35 = Helpers.GetSemanticKernel(weakGpt: true);
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts");
            var promptPlugins = kernel35.ImportPluginFromPromptDirectory(path);
            var ragOrNotRagPrompt = promptPlugins["RagOrNotRag"];

            // Route the step
            bool rag_search;
            {
                var message = Helpers.ChatHistoryToString(history, searchData.UserMessage);

                // invoke plugin to decide if the message should go to RAG or not
                var ragAsk = await kernel35.InvokeAsync<string>(ragOrNotRagPrompt, new() { ["chat"] = message });

                rag_search = ragAsk.Replace("```json\n", "").Replace("```", "").Trim().Contains("true", StringComparison.OrdinalIgnoreCase);
                logger.LogInformation("Rag search: " + (rag_search ? "yes" : "no"));
                logger.LogInformation("[DEBUG] Step: RoutingStep - RAG search decision: " + (rag_search ? "yes" : "no"));
            }

            if (rag_search)
            {
                // emit event: route
                await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.RagSearchRequested, Data = searchData, Visibility = KernelProcessEventVisibility.Public });
            }
            else
            {
                // emit event: route
                await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.NoRagSearchNeeded, Data = searchData, Visibility = KernelProcessEventVisibility.Public });
            }

        }
    }
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}