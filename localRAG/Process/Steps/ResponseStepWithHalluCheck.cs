using System.Collections.Generic;
using System.Linq;
using localRAG.Models;
using localRAG.Plugins;
using localRAG.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace localRAG.Process.Steps
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class ResponseStepWithHalluCheck : KernelProcessStep<ResponseState>
    {
        public static class Functions
        {
            public const string GetChatResponse = nameof(GetChatResponse);
            public const string ClearChatHistory = nameof(ClearChatHistory);
        }

        [KernelFunction(Functions.GetChatResponse)]
        public async Task GetChatResponseAsync(KernelProcessStepContext context, SearchData searchData, Kernel _kernel)
        {
            Console.WriteLine("[DEBUG] Step: ResponseStepWithHalluCheck - GetChatResponseAsync called");
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

            // Retrieve context chunks from chat history (assuming last user message contains context)
            var contextChunks = new List<string>();
            foreach (var msg in chatHist)
            {
                if (msg.Role == AuthorRole.User && msg.Content != null)
                {
                    contextChunks.Add(msg.Content);
                }
            }

            // Hallucination check
            bool isGrounded = HallucinationCheckPlugin.IsGrounded(response.Content, contextChunks, minOverlap: 3);
            Console.WriteLine($"[DEBUG] Step: ResponseStepWithHalluCheck - Hallucination check result: {(isGrounded ? "grounded" : "hallucination detected")}");

            // If no context is found, run a self-critique/fact-check LLM prompt
            if (contextChunks.Count == 0 && !string.IsNullOrWhiteSpace(response.Content))
            {
                Console.WriteLine("[DEBUG] Step: ResponseStepWithHalluCheck - No context found, running self-critique LLM prompt");
                var critiquePrompt = $"You are an expert fact-checker. Given the following answer, is it factual and verifiable? If not, explain why. If you are not sure, say 'I don't know.'\n\nAnswer: {response.Content}";
                var critiqueService = _kernel.Services.GetRequiredService<IChatCompletionService>();
                var critiqueResponse = await critiqueService.GetChatMessageContentAsync(critiquePrompt).ConfigureAwait(false);
                if (critiqueResponse != null && !string.IsNullOrWhiteSpace(critiqueResponse.Content))
                {
                    var critique = critiqueResponse.Content.ToLowerInvariant();
                    if (critique.Contains("not factual") || critique.Contains("not verifiable") || critique.Contains("hallucination") || critique.Contains("i don't know"))
                    {
                        response = new ChatMessageContent(AuthorRole.Assistant, "[Warning: This answer may be a hallucination or not verifiable. Fact-check: " + critiqueResponse.Content + "]\n" + response.Content);
                    }
                    else
                    {
                        response = new ChatMessageContent(AuthorRole.Assistant, "[Fact-check: " + critiqueResponse.Content + "]\n" + response.Content);
                    }
                }
            }
            else if (!isGrounded)
            {
                response = new ChatMessageContent(AuthorRole.Assistant, "[Warning: This answer may not be fully supported by the retrieved documents.]\n" + response.Content);
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
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
