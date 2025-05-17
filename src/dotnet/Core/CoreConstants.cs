namespace ActualChat;

public static partial class CoreConstants
{
    public static class AsyncMemoizer
    {
        public static readonly int TargetQueueSize = 16;
    }

    public static class MessageProcessor
    {
        public static readonly int QueueSize = 128;
        public static readonly TimeSpan ProcessCallTimeout = TimeSpan.FromSeconds(2);
    }
}
