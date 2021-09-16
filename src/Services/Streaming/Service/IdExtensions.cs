namespace ActualChat.Streaming
{
    public static class IdExtensions
    {
        public static string GetChannelName(this AudioRecordId audioRecordId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{audioRecordId}";
        public static string GetChannelName(this StreamId recordingId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{recordingId}";
    }
}