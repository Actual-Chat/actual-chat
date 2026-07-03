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
        public int ThroughputStepDownConsecutiveChecks { get; init; } = Constants.Video.ThroughputStepDownConsecutiveChecks;
        public int LatencyStepDownConsecutiveChecks { get; init; } = Constants.Video.LatencyStepDownConsecutiveChecks;
        // PLI rate limiting
        public double KeyFrameRequestCooldownMs { get; init; } = Constants.Video.KeyFrameRequestCooldown.TotalMilliseconds;
        // Warmup & codec switching
        public double PeerWarmupDurationMs { get; init; } = Constants.Video.PeerWarmupDuration.TotalMilliseconds;
        public double CodecSwitchHysteresisWindowMs { get; init; } = Constants.Video.CodecSwitchHysteresisWindow.TotalMilliseconds;
        // Egress-side layer fallback
        public double EgressStallThresholdMs { get; init; } = Constants.Video.EgressStallThreshold.TotalMilliseconds;
        public double EgressRecoveryWindowMs { get; init; } = Constants.Video.EgressRecoveryWindow.TotalMilliseconds;
        public int EgressGapFrameThreshold { get; init; } = Constants.Video.EgressGapFrameThreshold;
        // Stream management
        public int MaxCameraStreamsPerChat { get; init; } = Constants.Video.MaxCameraStreamsPerChat;
        public int PriorityActivationThreshold { get; init; } = Constants.Video.PriorityActivationThreshold;
        public double SilenceGracePeriodMs { get; init; } = Constants.Video.SilenceGracePeriod.TotalMilliseconds;
        public double ScreenCastKeepAlivePeriodMs { get; init; } = Constants.Video.ScreenCastKeepAlivePeriod.TotalMilliseconds;
        public double[] CameraLayerBaseBitratesKbps { get; init; } = VideoLayerDef.CameraLayers.Select(x => x.BaseBitrateKbps).ToArray();
        public double[] ScreenCastLayerBaseBitratesKbps { get; init; } = VideoLayerDef.ScreenCastLayers.Select(x => x.BaseBitrateKbps).ToArray();
        public VideoCodecDef[] CodecDefs { get; init; } = VideoCodecDef.All;
    }
}
