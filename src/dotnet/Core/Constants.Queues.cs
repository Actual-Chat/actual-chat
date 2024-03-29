namespace ActualChat;

public static partial class Constants
{
    public static class Queues
    {
        // Core
        public static readonly int AsyncMemoizerTargetQueueSize = 16;

        // Audio
        public static readonly int OpusStreamConverterQueueSize = 128;
        public static readonly int WebMStreamConverterQueueSize = 128;
        public static readonly int TrackPlayerCommandQueueSize = 8;
    }

    public static class MessageProcessing
    {
        public static readonly int QueueSize = 128;
        public static readonly TimeSpan ProcessCallTimeout = TimeSpan.FromSeconds(2);
    }
}
