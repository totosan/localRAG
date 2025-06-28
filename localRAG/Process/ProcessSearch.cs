using localRAG.Process.StepEvents;
using localRAG.Process.Steps;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Process
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public static class SearchProcess
    {
        private const string SYSTEM_PROMPT = """
                           You are a helpful assistant replying to user questions using information from your memory. You use the concept of RAG (Retreival augmented generation), to work with local documents.
                           Reply very briefly and concisely, get to the point immediately. Don't provide long explanations unless necessary.
                           Sometimes you don't have relevant memories so you reply saying you don't know, don't have the information.
                           For retrieving information to answer complex questions, you have to first plan your search strategy by deciding which steps to take.
                           Please first come up with a plan and then execute it. You can ask for help if you are stuck.
                           Always respond with the source' name and partition nr in Format [DocName:PartitionNr] if you provide information from a document.
                           """;
        internal static SearchData? _searchState { get; set; }

        public static ProcessBuilder CreateProcess()
        {

            ProcessBuilder processBuilder = new("SearchProcess");

            var rewriteStep = processBuilder.AddStepFromType<Steps.RewriteAskStep>();
            var routingStep = processBuilder.AddStepFromType<Steps.RoutingStep>();
            var ragSearchStep = processBuilder.AddStepFromType<Steps.LookupKernelmemoriesStep>();
            var responseStep = processBuilder.AddStepFromType<Steps.ResponseStepWithHalluCheck>();
            var renderStep = processBuilder.AddStepFromType<Steps.RenderResponsesStep>();

            processBuilder
                .OnInputEvent(RewriteAskStep.OutputEvents.RewriteUsersAskSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(rewriteStep, parameterName: "userInput"));

            processBuilder
                .OnInputEvent(ChatUserInputStep.OutputEvents.ReimportDocumentsSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(ragSearchStep, LookupKernelmemoriesStep.Functions.RemoveIndex));

            rewriteStep
                .OnEvent(RewriteAskStep.OutputEvents.RewriteUsersAskReceived)
                .SendEventTo(new ProcessFunctionTargetBuilder(routingStep, RoutingStep.Functions.RoutingStep, parameterName: "searchData"));
            routingStep
                .OnEvent(Steps.RoutingStep.OutputEvents.RagSearchRequested)
                .SendEventTo(new ProcessFunctionTargetBuilder(ragSearchStep, LookupKernelmemoriesStep.Functions.GetIntentOfAsk, parameterName: "searchData"));
            routingStep
                .OnEvent(Steps.RoutingStep.OutputEvents.NoRagSearchNeeded)
                .SendEventTo(new ProcessFunctionTargetBuilder(responseStep, ResponseStep.Functions.GetChatResponse, parameterName: "searchData"));
            ragSearchStep
                .OnEvent(Steps.LookupKernelmemoriesStep.OutputEvents.IntentsReceived)
                .SendEventTo(new ProcessFunctionTargetBuilder(ragSearchStep, LookupKernelmemoriesStep.Functions.GetMemoryData, parameterName: "searchData"));
            ragSearchStep
                .OnEvent(Steps.LookupKernelmemoriesStep.OutputEvents.MemoryDataReceived)
                .SendEventTo(new ProcessFunctionTargetBuilder(responseStep, ResponseStep.Functions.GetChatResponse, parameterName: "searchData"));

            responseStep
                .OnEvent(CommonEvents.ResponseToUserSend)
                .SendEventTo(new ProcessFunctionTargetBuilder(renderStep, RenderResponsesStep.Functions.RenderResponses, parameterName: "responseToRender"));
            return processBuilder;
        }
    }

    public class SearchData
    {
        public ChatHistory? ChatHistory { get; set; }
        public string UserMessage { get; set; } = string.Empty;
        public List<UserAsk> StandaloneQuestions { get; set; } = new();
        public List<string>? Intents { get; internal set; }
    }
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}