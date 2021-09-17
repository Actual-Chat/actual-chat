namespace ActualChat.Streaming.Server
{
    public static class StreamIdExt
    {
        public static string GetRedisChannelName(this StreamId streamId)
            => $"stream-{streamId.Value}";
    }
}
