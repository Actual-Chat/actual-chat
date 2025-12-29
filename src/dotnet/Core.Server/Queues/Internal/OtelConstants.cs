namespace ActualChat.Queues.Internal;

internal static class OtelConstants
{
    public const string MessagingProcessingStatus = "messaging.processing_status";
    public const string MessagingOperation = "messaging.operation";
    public const string MessagingMessageId = "messaging.message.id";
    public const string MessagingMessageType = "messaging.message.type";

    public static class ProcessingStatus {
        public const string Completed = nameof(Completed);
        public const string Postponed = nameof(Postponed);
        public const string Canceled = nameof(Canceled);
        public const string Failed = nameof(Failed);
    }
}
