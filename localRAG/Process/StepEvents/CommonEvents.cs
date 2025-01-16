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
        public static string ResponseToUserSend { get; set; } = nameof(ResponseToUserSend);
        public static string GetResponseToUserReceived { get; set; } = nameof(GetResponseToUserReceived);
        public static string ExitSend { get; set; } = nameof(ExitSend);
        public static string ExitReceived { get; set; } = nameof(ExitReceived);
    }
}