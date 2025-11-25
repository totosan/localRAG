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
    
    /// <summary>
    /// 🎤 SLIDE 10: Halluzinations-Prävention
    /// 
    /// Implements LLM-based hallucination detection to ensure generated answers
    /// are grounded in retrieved context, not fabricated by the model.
    /// 
    /// Demo Breakpoint: Line 84 (check LLM score result)
    /// Watch Variables: checkResult, isGrounded, contextContent
    /// 
    /// Talking Points:
    /// - "We use a separate GPT-3.5 model as a 'fact-checker'"
    /// - "Compares generated answer against retrieved context"
    /// - "Scores YES/NO - if NO, we flag the response with a warning"
    /// - "Fallback to simple keyword overlap for robustness"
    /// </summary>
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
            ChatMessageContent response = new(AuthorRole.Assistant, "I'm sorry, I don't have a response for that.");
            var userMessage = searchData.StandaloneQuestions.First().StandaloneQuestion;

            var chatHist = await _kernel.GetHistory().GetHistoryAsync();
            chatHist.Add(new(AuthorRole.User, userMessage));

            // If NO RAG was performed, we need to tell the model it's okay to answer from general knowledge
            // otherwise the strict system prompt might make it refuse to answer.
            if (!searchData.RagPerformed)
            {
                chatHist.Add(new(AuthorRole.System, "For this question, you do NOT need to use document memory. Please answer from your general knowledge."));
            }

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

            // Only perform hallucination check if RAG was actually performed
            bool performHallucinationCheck = searchData.RagPerformed;
            bool isGrounded = true;  // Default to grounded if no RAG
            
            #region Slide 10: Hallucination Check Logic
            
            if (performHallucinationCheck)
            {
                // Extract context from the last user message which contains the RAG context
                var lastUserMessage = chatHist.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? "";
                var contextStart = lastUserMessage.IndexOf("Context:\n");
                
                if (contextStart >= 0)
                {
                    // Extract just the context part, ignoring the user's question at the end
                    var contextContent = lastUserMessage.Substring(contextStart);
                    
                    // Use LLM-based hallucination check for better accuracy
                    var kernel35 = Helpers.GetSemanticKernel(weakGpt: false);
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "Plugins/Prompts");
                    var promptPlugins = kernel35.ImportPluginFromPromptDirectory(path);
                    
                    try 
                    {
                        var checkResult = await kernel35.InvokeAsync<string>(
                            promptPlugins["HalucinationCheckPlugin"], 
                            new() { ["question"] = contextContent, ["answer"] = response.Content }
                        );
                        
                        // Check if the result contains "YES" (grounded) or "NO" (hallucination)
                        // The prompt asks for "Score: YES" or "Score: NO"
                        isGrounded = checkResult != null && checkResult.Contains("Score: YES", StringComparison.OrdinalIgnoreCase);
                        
                        TraceLogger.Log($"[HallucinationCheck] LLM Result: {checkResult?.Substring(0, Math.Min(50, checkResult?.Length ?? 0))}... Grounded: {isGrounded}");
                    }
                    catch (Exception ex)
                    {
                        TraceLogger.Log($"[HallucinationCheck] LLM check failed: {ex.Message}. Fallback to keyword overlap.");
                        // Fallback to simple keyword overlap
                        isGrounded = HallucinationCheckPlugin.IsGrounded(response.Content, new List<string> { contextContent }, minOverlap: 3);
                    }
                    
                    // Warn if hallucination detected
                    if (!isGrounded)
                    {
                        response = new ChatMessageContent(AuthorRole.Assistant, "[Warning: This answer is not based on the retrieved documents.]\n" + response.Content);
                    }
                }
            }
            
            #endregion

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
