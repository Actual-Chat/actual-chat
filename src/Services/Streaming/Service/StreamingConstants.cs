namespace ActualChat.Streaming
{
    public static class StreamingConstants
    {
        public const string Completed = "completed";
        public const string MessageKey = "m";
        public const string StatusKey = "s";
        public const string StreamQueue = "queue";
        public const string AudioRecordingPrefix = "audio-rec";
        public const string AudioRecordingQueue = "audio-rec-queue";
        public const int EmptyStreamDelay = 250;

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
