namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        // Frame rate / cadence — derived everywhere downstream.
        public const int FrameRate = 30;
        public static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1d / FrameRate); // 33.333 ms

        // Target playback buffer (the only intentional live-video buffer).
        public const int TargetBufferSize = 10;
        public static readonly TimeSpan TargetBufferDuration =
            TimeSpan.FromSeconds((double)TargetBufferSize / FrameRate); // 333.333 ms

        // Keyframe cadence — KeyFramePeriod is the input; KeyFramePeriodSize is derived.
        public static readonly TimeSpan KeyFramePeriod = TimeSpan.FromSeconds(3);
        public static readonly int KeyFramePeriodSize = (int)(FrameRate * KeyFramePeriod.TotalSeconds); // 90

        // Buffer hysteresis around TargetBufferSize.
        public const int BufferHysteresisSize = TargetBufferSize / 2; // 5
        public const int MinBufferSize = TargetBufferSize - BufferHysteresisSize; // 5
        public const int MaxBufferSize = TargetBufferSize + BufferHysteresisSize; // 15

        // Server replay tail — duration is the input; size is derived.
        public static readonly TimeSpan ServerReplayTailDuration = TimeSpan.FromSeconds(1);
        public static readonly int ServerReplayTailSize = (int)(FrameRate * ServerReplayTailDuration.TotalSeconds); // 30

        // Max simulcast tiers by stream kind. Mirrors TS constants in
        // src/dotnet/UI.Blazor.App/Components/VideoPanel/simulcast-ladder.ts.
        // Webcam: up to 3 tiers (720p/360p/180p). Screencast: top + half-size.
        public const int WebcamMaxSimulcastTiers = 3;
        public const int ScreencastMaxSimulcastTiers = 2;
        public const int MaxSimulcastTiers = 3;

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

        // RPC stream flow control for video — doc-target values from
        // docs/video-pipeline.md "Constants" block: derived from TargetBufferSize.
        // 5-frame ack cadence (~165ms @ 30fps), 10-frame credit window
        // (~333ms outstanding). Real-time canSkipTo=isKeyFrame compaction
        // handles stalls by skipping to the latest decoder-safe frame.
        public const int RpcStreamAckPeriod = TargetBufferSize / 2; // 5
        public const int RpcStreamBufferSize = TargetBufferSize;    // 10
        // Memoizer retention is now duration-tracked, keyframe-span eviction
        // (VideoStreamMemoizer in Streaming.Service) bounded by
        // ServerReplayTailDuration — no count-based ceiling.

        // Latency measurement & quality adaptation. Cadence drives the per-stream
        // lag samples that feed PlaybackLagTracker (audio catch-up); 500 ms keeps
        // the policy responsive to drift without spamming JS↔.NET interop.
        public static readonly TimeSpan LatencyReportInterval = TimeSpan.FromMilliseconds(500);
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
        // 100ms ≈ 3× the 33ms/frame budget at 30fps. Matched to the JS-side
        // SLOW_DECODE_TIME_THRESHOLD_MS so server and client share one definition
        // of "decoder is slow". Previous 15ms tripped on healthy mid-tier decoders
        // and was effectively unused (the EvaluateQuality loop gated receiver-bound
        // classification behind IsNetworkSlow).
        public static readonly float HighDecodeTimeThresholdMs = 100f;
        public static readonly int HighBufferDepthThreshold = 10;     // Receiver's buffer is bloated

        // Over-delivery detection: HW encoder ignoring bitrate cap (e.g. HEVC VBR blowing past
        // target by 2.5×). Under-delivery is NOT a congestion signal — latency-vs-baseline
        // catches real congestion; encoder output is content-driven and routinely below target.
        public static readonly float ThroughputOverDeliveryRatio = 2.5f; // Step down when actual > 250% of target
        public static readonly int ThroughputStepDownConsecutiveChecks = 2; // Require 2 consecutive high checks
        // Latency-driven step-down hysteresis. Without this the publisher
        // ping-pongs between presets every QualityDecisionInterval (~2s) on a
        // single congestion blip — each switch reconfigures the WebCodecs
        // encoder + WebGPU downscaler and forces a keyframe, which is the very
        // load that prolongs the congestion. Two consecutive samples = ~4s of
        // sustained slowness before we step down. Step-up keeps its longer
        // QualityHysteresisWindow (5s) so we don't oscillate at the boundary.
        public static readonly int LatencyStepDownConsecutiveChecks = 2;

        // PLI rate limiting
        public static readonly TimeSpan KeyFrameRequestCooldown = TimeSpan.FromSeconds(5);

        // Warmup
        public static readonly TimeSpan PeerWarmupDuration = TimeSpan.FromSeconds(10);

        // Codec selection
        public static readonly TimeSpan CodecSwitchHysteresisWindow = TimeSpan.FromSeconds(10);

        // Egress-side spatial fallback: server-edge fast-reaction cap when a peer's
        // fan-out stalls or can't anchor a keyframe on its current spatial layer.
        // Bypasses the 2s latency-report cadence to react within one frame.
        public static readonly TimeSpan EgressStallThreshold = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan EgressRecoveryWindow = TimeSpan.FromSeconds(10);
        // Max frames skipped on the selected spatial layer before egress falls back.
        // ~5s at 30fps — covers up to 5 missed 1s-cadence keyframes.
        public static readonly int EgressGapFrameThreshold = 150;

        // Stream count limits
        public static readonly int MaxWebcamStreamsPerChat = 8;
        public static readonly int PriorityActivationThreshold = 6;
        public static readonly TimeSpan SilenceGracePeriod = TimeSpan.FromSeconds(30);
    }
}
