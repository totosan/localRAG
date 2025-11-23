using Elastic.Clients.Elasticsearch;
using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using System.Linq;

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
            TraceLogger.Log($"[RoutingStep] Evaluating if RAG search needed for: {searchData.UserMessage}");
            var historyProvider = _kernel.GetHistory();
            var history = await historyProvider.GetHistoryAsync();
            // Use stronger model for routing decisions (weakGpt: false)
            var kernel35 = Helpers.GetSemanticKernel(weakGpt: false);
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts");
            var promptPlugins = kernel35.ImportPluginFromPromptDirectory(path);
            var ragOrNotRagPrompt = promptPlugins["RagOrNotRag"];

            // Route the step
            bool rag_search;
            {
                var message = Helpers.ChatHistoryToString(history, searchData.UserMessage);
                var userMessageLower = searchData.UserMessage.ToLowerInvariant();
                
                // Strong heuristic: if message explicitly mentions documents or stored content, force RAG
                bool forcedRag = userMessageLower.Contains("document") 
                    || userMessageLower.Contains("summarize") 
                    || userMessageLower.Contains("file")
                    || userMessageLower.Contains("policy")
                    || userMessageLower.Contains("contract")
                    || userMessageLower.Contains("invoice")
                    || userMessageLower.Contains("imported-documents");

                // invoke plugin to decide if the message should go to RAG or not
                var ragAsk = await kernel35.InvokeAsync<string>(ragOrNotRagPrompt, new() { ["chat"] = message });
                
                // Log raw response for debugging
                TraceLogger.Log($"[RoutingStep] Raw LLM response: {ragAsk}");
                
                // Clean up response and try to parse JSON
                var cleaned = ragAsk.Replace("```json", "").Replace("```", "").Trim();
                
                // Try to parse as JSON first
                try
                {
                    var jsonDoc = JsonDocument.Parse(cleaned);
                    JsonElement element = jsonDoc.RootElement;
                    
                    // Handle array responses (take first element)
                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                    {
                        element = element[0];
                        TraceLogger.Log("[RoutingStep] Warning: LLM returned array, using first element");
                    }
                    
                    if (element.TryGetProperty("requiresRAG", out var requiresRag))
                    {
                        rag_search = requiresRag.GetBoolean();
                    }
                    else
                    {
                        // Fallback: check if response starts with true/false
                        rag_search = cleaned.StartsWith("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (JsonException ex)
                {
                    TraceLogger.Log($"[RoutingStep] JSON parse error: {ex.Message}");
                    // Fallback for non-JSON: look at first word only
                    var firstWord = cleaned.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    rag_search = firstWord.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                
                // Override with forced RAG if keywords detected
                if (forcedRag)
                {
                    TraceLogger.Log($"[RoutingStep] Keyword-based override: forcing RAG search (LLM said: {rag_search})");
                    rag_search = true;
                }
                
                logger.LogInformation("Rag search: " + (rag_search ? "yes" : "no"));
                logger.LogInformation("[DEBUG] Step: RoutingStep - RAG search decision: " + (rag_search ? "yes" : "no"));
                TraceLogger.Log($"[RoutingStep] LLM response: {ragAsk.Trim()}");
                TraceLogger.Log($"[RoutingStep] Decision: {(rag_search ? "RAG search REQUIRED" : "Direct answer (no RAG)")}" );
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