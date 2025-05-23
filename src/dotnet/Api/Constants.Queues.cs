namespace ActualChat;

public static partial class Constants
{
    public static class Queues
    {
        // Audio
        public static readonly int OpusStreamConverterQueueSize = 128;
        public static readonly int WebMStreamConverterQueueSize = 128;
        public static readonly int TrackPlayerCommandQueueSize = 8;
    }
}
