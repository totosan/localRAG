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
                userask = userask.Replace("```json\n", "").Replace("```", "").Trim();
                logger.LogInformation("Rewritten user ask: " + userask);

                rewrittenQuestions = JsonSerializer.Deserialize<List<UserAsk>>(userask);
                // if result is null take userinput as rewritten question, else take the result and order it by score, highest first!
                if (rewrittenQuestions == null)
                {
                    rewrittenQuestions = new List<UserAsk> { new UserAsk { StandaloneQuestion = userInput } };
                }
                else
                {
                    rewrittenQuestions = rewrittenQuestions.OrderByDescending(x => x.Score).ToList();
                }
            }
            catch (System.Exception e)
            {
                logger.LogError("Error in rewriting user ask: " + e.Message);
                logger.LogError($"\tUser ask: \n\t{rewrittenQuestions!.Aggregate("", (acc, item) => acc + "\n" + item.StandaloneQuestion)}");
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
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.