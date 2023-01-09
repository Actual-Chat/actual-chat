namespace ActualChat;

public static partial class Constants
{
    public static class Queues
    {
        // Core
        public static int AsyncMemoizerTargetQueueSize { get; } = 16;
        public static int MessageProcessorQueueDefaultSize { get; } = 128;
        public static int LocalCommandQueueDefaultSize { get; } = 1024;
        // Audio
        public static int OpusStreamAdapterQueueSize { get; } = 128;
        public static int WebMStreamAdapterQueueSize { get; } = 128;
        public static int TrackPlayerCommandQueueSize { get; } = 8;
    }
}
