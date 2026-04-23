namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StreamExpirationDelay = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxLiveDuration = TimeSpan.FromHours(8);

        // Watchdog: cancel PushVideo handler if no frame arrives within this window.
        // Webcam: 10s — tolerates brief sensor stalls (camera permission re-prompt,
        // OS-level camera swap, momentary USB hang) without killing the stream.
        public static readonly TimeSpan WebcamFrameSilenceTimeout = TimeSpan.FromSeconds(10);
        // Screencast: 3min — getDisplayMedia is change-driven. A user reading code
        // in a static IDE produces zero frames for extended periods. Client sends
        // heartbeat frames every ScreencastHeartbeatInterval during silence, so
        // this timeout only trips if the client itself is stuck/gone.
        public static readonly TimeSpan ScreencastFrameSilenceTimeout = TimeSpan.FromMinutes(3);

        // RPC stream flow control for video (30fps, 33ms frames).
        // Tuned for up to ~1s RTT: ackAdvance > ackPeriod + fps × RTT.
        public const int StreamAckPeriod = 64;
        public const int StreamAckAdvance = 192;
        public static readonly int RetentionBufferSize = 60; // ~2s at 30fps — bounds live server heap
        public static readonly int ReplayBufferSize = 30;   // ~1s at 30fps — bounded replay channel per consumer
        public static readonly int ConsumerBufferSize = 300; // ~10s at 30fps before slow consumer disconnect

        // Latency measurement & quality adaptation
        public static readonly TimeSpan LatencyReportInterval = TimeSpan.FromSeconds(2);
        // Absolute-latency fallbacks — used only before the per-peer baseline is
        // established (during warmup). Once BaselineLatencyMs is set, the delta-from-
        // baseline logic takes over so cross-continent peers with permanently high
        // but stable latency are NOT treated as slow. See PeerLatencyState.IsNetworkSlow.
        public static readonly float HighLatencyThresholdMs = 900f;
        public static readonly float LowLatencyThresholdMs = 300f;
        public static readonly float SkipToLiveThresholdMs = 3000f; // Client-side threshold for re-requesting stream
        public static readonly TimeSpan QualityDecisionInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan QualityHysteresisWindow = TimeSpan.FromSeconds(5);
        public static readonly int LatencyHistorySize = 5; // ~10s at 2s intervals
        public static readonly float PeerOutlierRatio = 0.5f;
        public static readonly float PeerOutlierRatioSmallCall = 0.34f; // 1 of 2 peers triggers step-down

        // Delta-from-baseline congestion detection. Step-down fires when a peer's
        // median latency exceeds baseline by both the absolute and multiplicative
        // margin (both must be true to avoid tripping on small baselines or small spikes).
        public static readonly float BaselineLatencyRiseAbsoluteMs = 200f;
        public static readonly float BaselineLatencyRiseMultiplier = 1.3f;
        // "Fast" = within this much above baseline. Allows step-up when link is stable.
        public static readonly float BaselineLatencyFastMarginMs = 100f;
        // EMA smoothing factor for the per-peer baseline. α=0.05 → ~20-sample time
        // constant, ≈40s at the 2s report interval. Slow enough that a transient
        // 10s spike doesn't materially move it.
        public static readonly float BaselineLatencyEmaAlpha = 0.05f;

        // Root-cause classification thresholds
        public static readonly float HighDecodeTimeThresholdMs = 15f; // Receiver's decoder is struggling
        public static readonly int HighBufferDepthThreshold = 10;     // Receiver's buffer is bloated

        // Over-delivery detection: HW encoder ignoring bitrate cap (e.g. HEVC VBR blowing past
        // target by 2.5×). Under-delivery is NOT a congestion signal — latency-vs-baseline
        // catches real congestion; encoder output is content-driven and routinely below target.
        public static readonly float ThroughputOverDeliveryRatio = 2.5f; // Step down when actual > 250% of target
        public static readonly int ThroughputStepDownConsecutiveChecks = 2; // Require 2 consecutive high checks

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
