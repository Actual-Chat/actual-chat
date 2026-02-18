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
        public static readonly float HighLatencyThresholdMs = 500f;
        public static readonly float LowLatencyThresholdMs = 200f;
        public static readonly float GopSkipThresholdMs = 1000f;
        public static readonly float GopSkipRecoveryMs = 500f;
        public static readonly TimeSpan QualityDecisionInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan QualityHysteresisWindow = TimeSpan.FromSeconds(15);
        public static readonly int LatencyHistorySize = 6; // ~30s at 5s intervals
        public static readonly float PeerOutlierRatio = 0.5f;
    }
}
