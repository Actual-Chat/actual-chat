namespace ActualChat.Streaming
{
    public static class IdExtensions
    {
        public static string GetChannelName(this AudioRecordId audioRecordId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{audioRecordId.Value}";
        public static string GetChannelName(this StreamId recordingId) 
            => $"{StreamingConstants.AudioRecordingPrefix}-{recordingId.Value}";
    }
}