using localRAG.Models;
using localRAG.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Process.Steps
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class ResponseStep : KernelProcessStep<ResponseState>
    {


        public static class Functions
        {
            public const string GetChatResponse = nameof(GetChatResponse);
            public const string ClearChatHistory = nameof(ClearChatHistory);
        }



        /// <summary>
        /// Generates a response from the chat completion service.
        /// </summary>
        /// <param name="context">The context for the current step and process. <see cref="KernelProcessStepContext"/></param>
        /// <param name="userMessage">The user message from a previous step.</param>
        /// <param name="_kernel">A <see cref="Kernel"/> instance.</param>
        /// <returns></returns>
        [KernelFunction(Functions.GetChatResponse)]
        public async Task GetChatResponseAsync(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            ChatMessageContent response = new(AuthorRole.Assistant, "I'm sorry, I don't have a response for that.");
            var userMessage = searchData.StandaloneQuestions.First().StandaloneQuestion;

            var chatHist = await _kernel.GetHistory().GetHistoryAsync();
            chatHist.Add(new(AuthorRole.User, userMessage));
            IChatCompletionService chatService = _kernel.Services.GetRequiredService<IChatCompletionService>();
            response = await chatService.GetChatMessageContentAsync(chatHist).ConfigureAwait(false);
            if (response == null)
            {
                throw new InvalidOperationException("Failed to get a response from the chat completion service.");
            }

            // Update state with the response
            chatHist.Add(response);

            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = StepEvents.CommonEvents.StartProcessSend,
                Visibility = KernelProcessEventVisibility.Public
            });
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = StepEvents.CommonEvents.ResponseToUserSend,
                Data = response,
                Visibility = KernelProcessEventVisibility.Public
            });
        }
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.