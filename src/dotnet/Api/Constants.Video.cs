namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StreamExpirationDelay = TimeSpan.FromSeconds(30);

        // RPC stream flow control for video (30fps, 33ms frames).
        // Tuned for up to ~1s RTT: ackAdvance > ackPeriod + fps × RTT.
        public const int StreamAckPeriod = 64;
        public const int StreamAckAdvance = 192;
        public static readonly int RetentionBufferSize = 150; // ~5s at 30fps
        public static readonly int ReplayBufferSize = 90;   // ~3s at 30fps — bounded replay channel per consumer
        public static readonly int ConsumerBufferSize = 300; // ~10s at 30fps before slow consumer disconnect

        // Latency measurement & quality adaptation
        public static readonly TimeSpan LatencyReportInterval = TimeSpan.FromSeconds(2);
        public static readonly float HighLatencyThresholdMs = 900f;
        public static readonly float LowLatencyThresholdMs = 300f;
        public static readonly float SkipToLiveThresholdMs = 3000f; // Client-side threshold for re-requesting stream
        public static readonly TimeSpan QualityDecisionInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan QualityHysteresisWindow = TimeSpan.FromSeconds(5);
        public static readonly int LatencyHistorySize = 5; // ~10s at 2s intervals
        public static readonly float PeerOutlierRatio = 0.5f;
        public static readonly float PeerOutlierRatioSmallCall = 0.34f; // 1 of 2 peers triggers step-down

        // Root-cause classification thresholds
        public static readonly float HighDecodeTimeThresholdMs = 15f; // Receiver's decoder is struggling
        public static readonly int HighBufferDepthThreshold = 10;     // Receiver's buffer is bloated

        // Throughput-based quality adaptation
        public static readonly float ThroughputStepDownRatio = 0.5f; // Step down when actual < 50% of target
        public static readonly float ThroughputOverDeliveryRatio = 2.5f; // Step down when actual > 250% of target
        public static readonly int ThroughputStepDownConsecutiveChecks = 2; // Require 2 consecutive low checks

        // PLI rate limiting
        public static readonly TimeSpan KeyFrameRequestCooldown = TimeSpan.FromSeconds(5);

        // Warmup
        public static readonly TimeSpan PeerWarmupDuration = TimeSpan.FromSeconds(10);

        // Codec selection
        public static readonly TimeSpan CodecSwitchHysteresisWindow = TimeSpan.FromSeconds(10);

        // Stream count limits
        public static readonly int MaxWebcamStreamsPerChat = 8;
        public static readonly int PriorityActivationThreshold = 6;
        public static readonly TimeSpan SilenceGracePeriod = TimeSpan.FromSeconds(30);
    }
}
