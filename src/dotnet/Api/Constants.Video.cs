namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StreamExpirationDelay = TimeSpan.FromSeconds(30);
        public static readonly int RetentionBufferSize = 150; // ~5s at 30fps
        public static readonly int ConsumerBufferSize = 300; // ~10s at 30fps before slow consumer disconnect

        // Latency measurement & quality adaptation
        public static readonly TimeSpan LatencyReportInterval = TimeSpan.FromSeconds(5);
        public static readonly float HighLatencyThresholdMs = 900f;
        public static readonly float LowLatencyThresholdMs = 300f;
        public static readonly float SkipToLiveThresholdMs = 3000f;
        public static readonly TimeSpan QualityDecisionInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan QualityHysteresisWindow = TimeSpan.FromSeconds(15);
        public static readonly int LatencyHistorySize = 6; // ~30s at 5s intervals
        public static readonly float PeerOutlierRatio = 0.5f;
        public static readonly float PeerOutlierRatioSmallCall = 0.34f; // 1 of 2 peers triggers step-down

        // Root-cause classification thresholds
        public static readonly float HighDecodeTimeThresholdMs = 15f; // Receiver's decoder is struggling
        public static readonly int HighBufferDepthThreshold = 10;     // Receiver's buffer is bloated

        // Warmup
        public static readonly TimeSpan PeerWarmupDuration = TimeSpan.FromSeconds(10);

        // Codec selection
        public static readonly TimeSpan CodecSwitchHysteresisWindow = TimeSpan.FromSeconds(10);
    }
}
