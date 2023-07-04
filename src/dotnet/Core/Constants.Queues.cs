namespace ActualChat;

public static partial class Constants
{
    public static class Queues
    {
        // Core
        public static int AsyncMemoizerTargetQueueSize { get; } = 16;
        public static int LocalCommandQueueDefaultSize { get; } = 1024;
        // Audio
        public static int OpusStreamConverterQueueSize { get; } = 128;
        public static int WebMStreamConverterQueueSize { get; } = 128;
        public static int TrackPlayerCommandQueueSize { get; } = 8;
    }

    public static class MessageProcessing
    {
        public static int QueueSize { get; } = 128;
        public static TimeSpan ProcessCallTimeout { get; } = TimeSpan.FromSeconds(2);
    }
}
