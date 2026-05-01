namespace ActualChat;

// ReSharper disable UnusedMember.Global

partial record AppConstants
{
    /// <summary>
    /// JS-interop snapshot of <see cref="Constants.Video"/>.
    /// Serialized to TS via BrowserInit and exposed there as the <c>VIDEO</c> field.
    /// Derived values (frame durations, buffer sizes, ms↔frame conversions, etc.)
    /// are computed TS-side in <c>initAppConstants</c> rather than shipped over the wire.
    /// </summary>
    public sealed record VideoConstants
    {
        // Frame rate / cadence
        public int FrameRate { get; init; } = Constants.Video.FrameRate;
        // Target playback buffer
        public int TargetBufferSize { get; init; } = Constants.Video.TargetBufferSize;
        // Keyframe cadence
        public double KeyFramePeriodMs { get; init; } = Constants.Video.KeyFramePeriod.TotalMilliseconds;
        // Server replay tail
        public double ServerReplayTailDurationMs { get; init; } = Constants.Video.ServerReplayTailDuration.TotalMilliseconds;
        // Stream lifecycle
        public double CancellationDelayMs { get; init; } = Constants.Video.CancellationDelay.TotalMilliseconds;
        public double StreamExpirationDelayMs { get; init; } = Constants.Video.StreamExpirationDelay.TotalMilliseconds;
        public double MaxLiveDurationMs { get; init; } = Constants.Video.MaxLiveDuration.TotalMilliseconds;
        // Frame silence watchdogs
        public double WebcamFrameSilenceTimeoutMs { get; init; } = Constants.Video.WebcamFrameSilenceTimeout.TotalMilliseconds;
        public double ScreencastFrameSilenceTimeoutMs { get; init; } = Constants.Video.ScreencastFrameSilenceTimeout.TotalMilliseconds;
        // RPC stream flow control
        public int RpcStreamAckPeriod { get; init; } = Constants.Video.RpcStreamAckPeriod;
        public int RpcStreamBufferSize { get; init; } = Constants.Video.RpcStreamBufferSize;
        public int RetentionBufferSize { get; init; } = Constants.Video.RetentionBufferSize;
        public int ConsumerBufferSize { get; init; } = Constants.Video.ConsumerBufferSize;
        // Latency & quality adaptation
        public double LatencyReportIntervalMs { get; init; } = Constants.Video.LatencyReportInterval.TotalMilliseconds;
        public float HighLatencyThresholdMs { get; init; } = Constants.Video.HighLatencyThresholdMs;
        public float LowLatencyThresholdMs { get; init; } = Constants.Video.LowLatencyThresholdMs;
        public float SkipToLiveThresholdMs { get; init; } = Constants.Video.SkipToLiveThresholdMs;
        public double QualityDecisionIntervalMs { get; init; } = Constants.Video.QualityDecisionInterval.TotalMilliseconds;
        public double QualityHysteresisWindowMs { get; init; } = Constants.Video.QualityHysteresisWindow.TotalMilliseconds;
        public int LatencyHistorySize { get; init; } = Constants.Video.LatencyHistorySize;
        public float PeerOutlierRatio { get; init; } = Constants.Video.PeerOutlierRatio;
        public float PeerOutlierRatioSmallCall { get; init; } = Constants.Video.PeerOutlierRatioSmallCall;
        // Baseline latency
        public float BaselineLatencyRiseAbsoluteMs { get; init; } = Constants.Video.BaselineLatencyRiseAbsoluteMs;
        public float BaselineLatencyRiseMultiplier { get; init; } = Constants.Video.BaselineLatencyRiseMultiplier;
        public float BaselineLatencyFastMarginMs { get; init; } = Constants.Video.BaselineLatencyFastMarginMs;
        public float BaselineLatencyEmaAlpha { get; init; } = Constants.Video.BaselineLatencyEmaAlpha;
        // Root-cause classification
        public float HighDecodeTimeThresholdMs { get; init; } = Constants.Video.HighDecodeTimeThresholdMs;
        public int HighBufferDepthThreshold { get; init; } = Constants.Video.HighBufferDepthThreshold;
        // Over-delivery detection
        public float ThroughputOverDeliveryRatio { get; init; } = Constants.Video.ThroughputOverDeliveryRatio;
        public int ThroughputStepDownConsecutiveChecks { get; init; } = Constants.Video.ThroughputStepDownConsecutiveChecks;
        public int LatencyStepDownConsecutiveChecks { get; init; } = Constants.Video.LatencyStepDownConsecutiveChecks;
        // PLI rate limiting
        public double KeyFrameRequestCooldownMs { get; init; } = Constants.Video.KeyFrameRequestCooldown.TotalMilliseconds;
        // Warmup & codec switching
        public double PeerWarmupDurationMs { get; init; } = Constants.Video.PeerWarmupDuration.TotalMilliseconds;
        public double CodecSwitchHysteresisWindowMs { get; init; } = Constants.Video.CodecSwitchHysteresisWindow.TotalMilliseconds;
        // Egress-side spatial fallback
        public double EgressStallThresholdMs { get; init; } = Constants.Video.EgressStallThreshold.TotalMilliseconds;
        public double EgressRecoveryWindowMs { get; init; } = Constants.Video.EgressRecoveryWindow.TotalMilliseconds;
        public int EgressGapFrameThreshold { get; init; } = Constants.Video.EgressGapFrameThreshold;
        // Simulcast & stream management
        public int MinMembersForSimulcast { get; init; } = Constants.Video.MinMembersForSimulcast;
        public int MaxWebcamStreamsPerChat { get; init; } = Constants.Video.MaxWebcamStreamsPerChat;
        public int PriorityActivationThreshold { get; init; } = Constants.Video.PriorityActivationThreshold;
        public double SilenceGracePeriodMs { get; init; } = Constants.Video.SilenceGracePeriod.TotalMilliseconds;
    }
}
