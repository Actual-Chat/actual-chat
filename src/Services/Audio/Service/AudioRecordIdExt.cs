namespace ActualChat.Audio
{
    public static class AudioRecordIdExt
    {
        public static string GetRedisChannelName(this AudioRecordId audioRecordId)
            => $"audio-{(string) audioRecordId}";
    }
}
