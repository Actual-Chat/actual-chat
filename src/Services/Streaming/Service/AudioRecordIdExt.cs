namespace ActualChat.Streaming
{
    public static class AudioRecordIdExt
    {
        public static string GetRedisChannelName(this AudioRecordId audioRecordId)
            => $"{StreamingConstants.AudioRecordingPrefix}-{audioRecordId.Value}";
    }
}
