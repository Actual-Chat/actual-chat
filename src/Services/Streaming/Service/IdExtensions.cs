namespace ActualChat.Streaming
{
    public static class IdExtensions
    {
        public static string GetChannelName(this RecordingId recordingId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{recordingId.Value}";
        public static string GetChannelName(this StreamId recordingId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{recordingId.Value}";
    }
}