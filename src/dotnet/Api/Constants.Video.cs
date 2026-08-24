namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        // Frame rate / cadence — derived everywhere downstream.
        public const int FrameRate = 30;
        // Camera capture rate on mobile devices; capture→convert→encode power is
        // roughly linear in fps, and 24 fps is visually equivalent in a chat.
        // Buffer/pacing math stays derived from FrameRate (30) — the receiver
        // present-pacer tolerates below-constant source fps.
        public const int MobileFrameRate = 24;
        public static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1d / FrameRate); // 33.333 ms

        // Target playback buffer (the only intentional live-video buffer).
        public const int TargetBufferSize = 6; // jitter buffer depth (frames)
        // 200 ms — below the audio buffer so audio lands slightly behind video
        // (the safe side); trades jitter headroom for latency.
        public static readonly TimeSpan TargetBufferSpan =
            TimeSpan.FromSeconds((double)TargetBufferSize / FrameRate);
        public static readonly double TargetBufferSpanMs = TargetBufferSpan.TotalMilliseconds;

        // Playback verdict thresholds (compared against per-tick EMAs of the
        // same `double Ms` type — direct comparison, no TimeSpan conversion).
        // BufferDurationTooLowMs sits at ⅓ of target so per-tick rounding
        // noise around the steady-state buffer can't flip the verdict.
        public static readonly double BufferDurationTooLowMs = TargetBufferSpanMs / 3;
        public static readonly double BufferDurationTooHighMs = TargetBufferSpanMs * 1.5;

        // Recording verdict thresholds — sender-ack age bands.
        public static readonly double LastAckBadMs = 2000;
        public static readonly double LastAckGoodMs = 500;

        // BandwidthEstimator initial ceilings (bytes/sec). Big enough to run
        // the baseline configuration without an early downgrade; the estimator
        // refines from the first real signal.
        public const long InitialOutboundCeilingBps = 375_000;  // ~3 Mbps
        public const long InitialInboundCeilingBps = 1_000_000; // ~8 Mbps

        // Vestigial: reference values for the continuous-penalty signalLevel
        // the QC legs used before SenderHealthClassifier / ReceiverHealthClassifier
        // took over. Nothing reads them today — the live thresholds are the
        // classifier defaults. See docs/live-video/08-quality-control.md.
        public const double DropOkSender = 0.20;
        public const double DropBadSender = 0.50;
        public const double DropOkReceiver = 0.20;
        public const double DropBadReceiver = 0.50;
        public const double PlaybackRateOk = 0.90;
        public const double PlaybackRateBad = 0.00;
        public const double AckOkMs = 500;
        public const double AckBadMs = 2000;
        // Encoder throughput-deficit thresholds (0..1, 0 = encoder keeps
        // pace with capture, 1 = encoder emits nothing). Consumed by the
        // sender-side EncodingCap for spatial-layer demote/restore.
        public const double EncOkDeficit = 0.05;
        public const double EncBadDeficit = 0.20;

        // Keyframe cadence — KeyFramePeriod is the input; KeyFramePeriodSize is derived.
        public static readonly TimeSpan KeyFramePeriod = TimeSpan.FromSeconds(3);
        public static readonly int KeyFramePeriodSize = (int)(FrameRate * KeyFramePeriod.TotalSeconds); // 90

        // Buffer hysteresis around TargetBufferSize.
        public const int BufferHysteresisSize = TargetBufferSize / 2; // 5
        public const int MinBufferSize = TargetBufferSize - BufferHysteresisSize; // 5
        public const int MaxBufferSize = TargetBufferSize + BufferHysteresisSize; // 15

        // Server replay tail — must span at least one keyframe interval so a
        // late subscriber's Replay window contains the latest keyframe anchor
        // (memoizer eviction preserves the anchor; Replay just has to be wide
        // enough to surface it). 10% margin over KeyFramePeriod absorbs minor
        // jitter in the sender's keyframe cadence.
        public static readonly TimeSpan ServerReplayTailDuration =
            TimeSpan.FromSeconds(KeyFramePeriod.TotalSeconds * 1.1);
        // Simulcast streams interleave the widest layer ladder into one
        // chain at VideoLayerDef.MaxLayerCount × FrameRate frames per second. Without the
        // layer multiplier the count cap clipped the Replay window to ~1.1 s of
        // wall-time, excluding the keyframe anchor VideoStreamMemoizer preserves
        // at the chain head and forcing GetVideoRaw's SkipWhile to drain
        // hundreds of deltas before the next live KF arrives.
        public static readonly int ServerReplayTailSize =
            VideoLayerDef.MaxLayerCount
            * FrameRate
            * (int)Math.Ceiling(ServerReplayTailDuration.TotalSeconds);
        // = 3 × 30 × 4 = 360 frames (~4 s wall-time per layer at full cadence)

        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StreamExpirationDelay = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxLiveDuration = TimeSpan.FromHours(8);

        // Stream-silence watchdog: counts incoming bundles per fixed
        // interval and cancels the PushVideo handler when there are zero
        // bundles for K consecutive intervals. With 5 s × 2 = 10 s total,
        // this catches "browser closed without a clean WebSocket close"
        // fast enough to free the single-screencast-per-chat slot for
        // another publisher. Replaces the older split CameraFrameSilenceTimeout
        // / ScreenCastFrameSilenceTimeout pair — cleaner state and unified
        // for both source kinds.
        public static readonly TimeSpan StreamSilenceCheckInterval = TimeSpan.FromSeconds(5);
        public static readonly int StreamSilenceMaxConsecutiveZeroIntervals = 2;

        // Screencast idle keepalive: getDisplayMedia delivers frames only when
        // the captured content changes, so a static screen would starve the
        // silence watchdog above and tear the stream down mid-share. The sender
        // re-emits the last captured frame at this cadence while idle; must stay
        // well below StreamSilenceCheckInterval × StreamSilenceMaxConsecutiveZeroIntervals.
        public static readonly TimeSpan ScreenCastKeepAlivePeriod = TimeSpan.FromSeconds(1);

        // RPC stream flow control for video. 5-frame ack cadence (~165ms @ 30fps).
        // The credit window must exceed the receiver's skip-to-live threshold
        // (TargetBufferSpanMs × 3 ≈ 600ms): a consumer that subscribed
        // behind the live edge (replay-tail keyframe) only catches up by pulling
        // the backlog into its buffer until the span trips skip-to-live. A window
        // below that threshold caps the buffer too low for the skip to ever fire,
        // so the consumer trails the server forever. 45 frames ≈ 1.5s of headroom.
        // Real-time canSkipTo=isKeyFrame compaction still handles sender stalls.
        public const int RpcStreamAckPeriod = 5;
        public const int RpcStreamAckAdvance = 45;
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

        public static readonly int ThroughputStepDownConsecutiveChecks = 2; // Require 2 consecutive high checks
        // Latency-driven step-down hysteresis. Without this the publisher
        // ping-pongs between presets every QualityDecisionInterval (~2s) on a
        // single congestion blip — each switch reconfigures the WebCodecs
        // encoder + WebGPU downscaler and forces a keyframe, which is the very
        // load that prolongs the congestion. Two consecutive samples = ~4s of
        // sustained slowness before we step down. Step-up keeps its longer
        // QualityHysteresisWindow (5s) so we don't oscillate at the boundary.
        public static readonly int LatencyStepDownConsecutiveChecks = 2;

        public static readonly TimeSpan KeyFrameRequestCooldown = KeyFramePeriod / 3;

        // Warmup
        public static readonly TimeSpan PeerWarmupDuration = TimeSpan.FromSeconds(10);

        // Codec selection
        public static readonly TimeSpan CodecSwitchHysteresisWindow = TimeSpan.FromSeconds(10);

        // Egress-side layer fallback: server-edge fast-reaction cap when a peer's
        // fan-out stalls or can't anchor a keyframe on its current layer.
        // Bypasses the 2s latency-report cadence to react within one frame.
        public static readonly TimeSpan EgressStallThreshold = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan EgressRecoveryWindow = TimeSpan.FromSeconds(10);
        // Max frames skipped on the selected layer before egress falls back.
        // ~5s at 30fps — covers up to 5 missed 1s-cadence keyframes.
        public static readonly int EgressGapFrameThreshold = 150;

        // Stream count limits
        public static readonly int MaxCameraStreamsPerChat = 8;
        public static readonly int PriorityActivationThreshold = 6;
        public static readonly TimeSpan SilenceGracePeriod = TimeSpan.FromSeconds(30);

        // Video session idle monitor (ChatVideoUI.IdleMonitor)
        public static readonly TimeSpan SessionInactivityTimeout = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan SessionConfirmInterval = TimeSpan.FromMinutes(60);
        public static readonly TimeSpan SessionConfirmModalTimeout = TimeSpan.FromMinutes(1);
        // How long after the last local VAD hit we still treat an ongoing
        // own-author transcription as "user is speaking on THIS device".
        // Wider than VAD's inter-segment pauses so continuous speech keeps
        // bumping; narrow enough that talking on another device releases
        // this device's recording within a sane window.
        public static readonly TimeSpan VadActiveGrace = TimeSpan.FromSeconds(30);
    }
}
