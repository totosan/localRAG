using localRAG.Models;
using localRAG.Process.StepEvents;
using Microsoft.SemanticKernel;

namespace localRAG.Process.Steps;
#pragma warning disable SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class ChatUserInputStep : KernelProcessStep<UserInputState>
{
    public static class Functions
    {
        public const string GetUserInput = nameof(GetUserInput);
    }

    public class OutputEvents
    {
        public static string UsersChatInputReceived { get; internal set; } = nameof(UsersChatInputReceived);
        public static string ChatLoopSend { get; internal set; } = nameof(ChatLoopSend);
        public static string ClearChatHistorySend { get; internal set; } = nameof(ClearChatHistorySend);
        public static string RemoveIndexSend { get; internal set; } = nameof(RemoveIndexSend);
        public static string ReimportDocumentsSend { get; internal set; } = nameof(ReimportDocumentsSend);
        public static string GenerateIntentsSend { get; internal set; } = nameof(GenerateIntentsSend);
    }

    /// <summary>
    /// The state object for the user input step. This object holds the user inputs and the current input index.
    /// </summary>
    private UserInputState? _state;

    public override ValueTask ActivateAsync(KernelProcessStepState<UserInputState> state)
    {
        _state = state.State;
        return ValueTask.CompletedTask;
    }

    internal string GetNextUserMessage()
    {
        if (_state != null && _state.CurrentInputIndex >= 0 && _state.CurrentInputIndex < this._state.UserInputs.Count)
        {
            var userMessage = this._state!.UserInputs[_state.CurrentInputIndex];
            _state.CurrentInputIndex++;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"USER: {userMessage}");
            Console.ResetColor();

            return userMessage;
        }

        Console.WriteLine("SCRIPTED_USER_INPUT: No more scripted user messages defined, returning empty string as user message");
        return string.Empty;
    }

    [KernelFunction(Functions.GetUserInput)]
    public virtual async ValueTask GetUserInputAsync(KernelProcessStepContext context, Kernel _kernel)
    {
        Console.WriteLine("[DEBUG] Step: ChatUserInputStep - GetUserInputAsync called");
        Console.WriteLine("---------");
        Console.WriteLine("Enter a question or type 'exit' to quit:");
        var userInput = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(userInput))
        { return; }
        else
        {
            if (userInput.StartsWith("/"))
            {
                switch (userInput.ToLower())
                {
                    case "/q":
                    case "/exit":
                        await context.EmitEventAsync(new() { Id = CommonEvents.ExitSend });
                        return;
                    case "/clear":
                        await context.EmitEventAsync(new() { Id = OutputEvents.ClearChatHistorySend });
                        return;
                    case "/ri":
                    case "/removeindex":
                        await context.EmitEventAsync(new() { Id = OutputEvents.RemoveIndexSend });
                        await context.EmitEventAsync(new() { Id = OutputEvents.ClearChatHistorySend });
                        return;
                    case "/reimport":
                    case "/im":
                        await context.EmitEventAsync(new() { Id = OutputEvents.ReimportDocumentsSend });
                        await context.EmitEventAsync(new() { Id = OutputEvents.ClearChatHistorySend });
                        return;
                    case "/gi":
                    case "/generateintents":
                        await context.EmitEventAsync(new() { Id = OutputEvents.GenerateIntentsSend });
                        return;
                    case "/h":
                    case "/help":
                        Console.WriteLine("Commands:");
                        Console.WriteLine("\t/exit - Exit the program");
                        Console.WriteLine("\t/clear - Clear the chat history");
                        Console.WriteLine("\t/removeindex - Delete all indexes");
                        Console.WriteLine("\t/reimport - Reimport all documents");
                        await context.EmitEventAsync(new() { Id = OutputEvents.ChatLoopSend });
                        return;
                    default:
                        Console.WriteLine("Unknown command. Type /help for a list of commands.");
                        await context.EmitEventAsync(new() { Id = OutputEvents.ChatLoopSend });
                        return;
                }
            }
        }

        _state.UserInputs.Add(userInput);
        _state.CurrentInputIndex++;

        // emitting userInputReceived event
        await context.EmitEventAsync(new() { Id = OutputEvents.UsersChatInputReceived, Data = userInput });
    }
}
#pragma warning restore SKEXP0080 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.