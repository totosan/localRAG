using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace localRAG.Process.Steps
{
    public class DatasourceMaintenanceStep:KernelProcessStep
    {
        public static class Functions
        {
            public const string ClearChatHistory = nameof(ClearChatHistory);
            public const string RemoveIndex = nameof(RemoveIndex);
            public const string ReimportDocuments = nameof(ReimportDocuments);
        }
        public static class OutputEvents
        {
            public static string ClearChatHistorySend { get; internal set; } = nameof(ClearChatHistorySend);
            public static string RemoveIndexSend { get; internal set; } = nameof(RemoveIndexSend);
            public static string ReimportDocumentsSend { get; internal set; } = nameof(ReimportDocumentsSend);
        }
        private readonly ILogger<DatasourceMaintenanceStep> logger;
        public DatasourceMaintenanceStep(ILogger<DatasourceMaintenanceStep> logger)
        {
            this.logger = logger;
        }
        [KernelFunction(Functions.ClearChatHistory)]
        public async Task ClearChatHistoryAsync(KernelProcessStepContext context)
        {
            //await Helpers.ClearChatHistoryAsync();
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.ClearChatHistorySend });
        }
        [KernelFunction(Functions.RemoveIndex)]
        public async Task RemoveIndexAsync(KernelProcessStepContext context)
        {
            //await Helpers.RemoveAllIndexsAsync();
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.RemoveIndexSend });
        }
        [KernelFunction(Functions.ReimportDocuments)]
        public async Task ReimportDocumentsAsync(KernelProcessStepContext context)
        {
            //await Helpers.ReimportDocumentsAsync();
            await context.EmitEventAsync(new KernelProcessEvent { Id = OutputEvents.ReimportDocumentsSend });
        }
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
