namespace ActualChat.Queues.Internal;

internal static class OtelConstants
{
    public const string ProcessingStatusTag = "queued_command.processing_status";

    public static class ProcessingStatus {
        public const string Completed = nameof(Completed);
        public const string Postponed = nameof(Postponed);
        public const string Canceled = nameof(Canceled);
        public const string Failed = nameof(Failed);
    }
}
