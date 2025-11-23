using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using localRAG.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using MongoDB.Driver.Linq;

namespace localRAG.Process.Steps
{
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public class RewriteAskStep : KernelProcessStep
    {

        public static class Functions
        {
            public const string RewriteAsk = nameof(RewriteAsk);
        }

        public class OutputEvents
        {
            public static string RewriteUsersAskSend { get; internal set; } = nameof(RewriteUsersAskSend);
            public static string RewriteUsersAskReceived { get; internal set; } = nameof(RewriteUsersAskReceived);
        }

        [KernelFunction(Functions.RewriteAsk)]
        public async Task RewriteAskAsync(KernelProcessStepContext context, string userInput, Kernel _kernel)
        {
            Console.WriteLine("[DEBUG] Step: RewriteAskStep - RewriteAskAsync called");
            var logger = _kernel.GetRequiredService<ILogger<RewriteAskStep>>();
            var chatHist = await _kernel.GetHistory().GetHistoryAsync();
            var kernel = _kernel;
            var promptPlugins = kernel.Plugins.First(x => x.Name == "Prompts");
            kernel.Plugins.First(x => x.Name == "Prompts");
            var rewriteUserAskPrompt = promptPlugins["RewriteUserAskPlugin"];

            List<UserAsk> rewrittenQuestions = new();
            var messages = Helpers.ChatHistoryToString(chatHist, userInput);
            try
            {
                var userask = await kernel.InvokeAsync<string>(rewriteUserAskPrompt, new() { ["question"] = messages });
                userask = SanitizePluginResponse(userask);

                if (!LooksLikeJson(userask))
                {
                    LogMalformedPayload(logger, userask);
                    rewrittenQuestions = [new UserAsk { StandaloneQuestion = userInput }];
                }
                else
                {
                    rewrittenQuestions = JsonSerializer.Deserialize<List<UserAsk>>(userask, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<UserAsk>();
                }

                if (rewrittenQuestions.Count == 0)
                {
                    rewrittenQuestions = [new UserAsk { StandaloneQuestion = userInput }];
                }
                else
                {
                    rewrittenQuestions = rewrittenQuestions
                        .Where(q => !string.IsNullOrWhiteSpace(q?.StandaloneQuestion))
                        .OrderByDescending(x => x.Score)
                        .DefaultIfEmpty(new UserAsk { StandaloneQuestion = userInput })
                        .ToList();
                }

                logger.LogInformation("[DEBUG] Step: RewriteAskStep - Rewritten questions produced");
            }
            catch (JsonException jsonEx)
            {
                logger.LogWarning(jsonEx, "RewriteUserAskPlugin returned invalid JSON payload, using fallback.");
                rewrittenQuestions = [new UserAsk { StandaloneQuestion = userInput }];
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "Unexpected error while rewriting user ask, falling back to original question.");
                rewrittenQuestions = [new UserAsk { StandaloneQuestion = userInput }];
            }

            var searchData = new SearchData
            {
                ChatHistory = chatHist,
                StandaloneQuestions = rewrittenQuestions,
                UserMessage = userInput
            };
            // emit event: rewriteAsk
            await context.EmitEventAsync(
                new KernelProcessEvent
                {
                    Id = OutputEvents.RewriteUsersAskReceived,
                    Data = searchData
                });
        }

        private static string SanitizePluginResponse(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            var cleaned = payload
                .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Strip <think>...</think> reasoning blocks that some models emit
            var thinkStartIndex = cleaned.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (thinkStartIndex >= 0)
            {
                var thinkEndIndex = cleaned.IndexOf("</think>", thinkStartIndex, StringComparison.OrdinalIgnoreCase);
                if (thinkEndIndex >= 0)
                {
                    // Remove everything from <think> to </think> inclusive
                    cleaned = cleaned.Remove(thinkStartIndex, thinkEndIndex - thinkStartIndex + "</think>".Length).Trim();
                }
            }

            return cleaned;
        }

        private static bool LooksLikeJson(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var trimmed = payload.TrimStart();
            return trimmed.StartsWith("[") || trimmed.StartsWith("{");
        }

        private static void LogMalformedPayload(ILogger logger, string payload)
        {
            var preview = payload.Length > 300 ? payload[..300] + "..." : payload;
            logger.LogWarning("RewriteUserAskPlugin did not return JSON. Preview: {Preview}", preview);
            TraceLogger.Log($"RewriteAskStep received non-JSON payload: {preview}");
        }
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.