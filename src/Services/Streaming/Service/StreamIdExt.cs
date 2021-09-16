namespace ActualChat.Streaming
{
    public static class StreamIdExt
    {
        public static string GetRedisChannelName(this StreamId recordingId)
            => $"{StreamingConstants.AudioRecordingPrefix}-{recordingId.Value}";
    }
}
