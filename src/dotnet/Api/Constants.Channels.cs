namespace ActualChat;

public static partial class Constants
{
    public static class Channels
    {
        // Audio
        public static readonly int OpusStreamConverterQueueSize = 128;
        public static readonly int WebMStreamConverterQueueSize = 128;
        public static readonly int TrackPlayerCommandQueueSize = 8;
        public static readonly BoundedChannelOptions OpusStreamConverterChannelOptions
            = new(OpusStreamConverterQueueSize) {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            };
        public static readonly BoundedChannelOptions WebMStreamConverterChannelOptions
            = new(WebMStreamConverterQueueSize) {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            };
        public static readonly BoundedChannelOptions TrackPlayerCommandChannelOptions
            = new(TrackPlayerCommandQueueSize) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            };
    }
}
