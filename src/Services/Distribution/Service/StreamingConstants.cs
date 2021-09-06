namespace ActualChat.Distribution
{
    public static class StreamingConstants
    {
        public const string Completed = "completed";
        public const string MessageKey = "m";
        public const string StatusKey = "s";
        public const string Queue = "queue";
        public const string AudioRecordingPrefix = "audio-rec";
        public const string AudioRecordingQueue = "audio-rec-queue";
        public const int EmptyStreamDelay = 100;

        public static string BuildChannelName(RecordingId recordingId)
        {
            return $"{AudioRecordingPrefix}-{recordingId.Value}";
        }
        
        public static string BuildChannelName(StreamId recordingId)
        {
            return $"{AudioRecordingPrefix}-{recordingId.Value}";
        }
    }
}
