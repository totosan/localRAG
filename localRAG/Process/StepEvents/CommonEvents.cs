namespace localRAG.Process.StepEvents
{
    public class CommonEvents
    {
        // step events:
        // - Start Process
        // - Get users chat input
        // - Rewrite users ask
        // - Get route (search is RAG or not)
        // - Get intent of ask
        // - Get result from RAG
        // - Get result from chat history
        // - Get response to user
        // - exit

        public static string StartProcessSend { get; set; } = nameof(StartProcessSend);
        public static string StartProcessReceived { get; set; } = nameof(StartProcessReceived);
        public static string UsersChatInputSend { get; set; } = nameof(UsersChatInputSend);
        public static string UsersChatInputReceived { get; set; } = nameof(UsersChatInputReceived);
        public static string RewriteUsersAskSend { get; set; } = nameof(RewriteUsersAskSend);
        public static string RewriteUsersAskReceived { get; set; } = nameof(RewriteUsersAskReceived);
        public static string GetRouteSend { get; set; } = nameof(GetRouteSend);
        public static string GetRouteReceived { get; set; } = nameof(GetRouteReceived);
        public static string GetIntentOfAskSend { get; set; } = nameof(GetIntentOfAskSend);

        public static string GetResultFromRAGSend { get; set; } = nameof(GetResultFromRAGSend);
        public static string GetResultFromRAGReceived { get; set; } = nameof(GetResultFromRAGReceived);
        public static string GetResultFromChatHistorySend { get; set; } = nameof(GetResultFromChatHistorySend);
        public static string GetResultFromChatHistoryReceived { get; set; } = nameof(GetResultFromChatHistoryReceived);
        public static string ResponseToUserSend { get; set; } = nameof(ResponseToUserSend);
        public static string GetResponseToUserReceived { get; set; } = nameof(GetResponseToUserReceived);
        public static string ExitSend { get; set; } = nameof(ExitSend);
        public static string ExitReceived { get; set; } = nameof(ExitReceived);
    }
}