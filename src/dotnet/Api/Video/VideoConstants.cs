namespace ActualChat.Video;

/// <summary>
/// JS-interop snapshot of <see cref="Constants.Video"/>.
/// Serialized to TS via BrowserInit and exposed there as the <c>VIDEO</c> field.
/// </summary>
public sealed record VideoConstants(
    // Frame rate / cadence
    int FrameRate,
    double FrameDurationMs,
    // Target playback buffer
    int TargetBufferSize,
    double TargetBufferDurationMs,
    // Keyframe cadence
    double KeyFramePeriodMs,
    int KeyFramePeriodSize,
    // Buffer hysteresis
    int BufferHysteresisSize,
    int MinBufferSize,
    int MaxBufferSize,
    // Server replay tail
    double ServerReplayTailDurationMs,
    int ServerReplayTailSize,
    // Stream lifecycle
    double CancellationDelayMs,
    double StreamExpirationDelayMs,
    double MaxLiveDurationMs,
    // Frame silence watchdogs
    double WebcamFrameSilenceTimeoutMs,
    double ScreencastFrameSilenceTimeoutMs,
    // RPC stream flow control
    int RpcStreamAckPeriod,
    int RpcStreamBufferSize,
    int RetentionBufferSize,
    int ConsumerBufferSize,
    // Latency & quality adaptation
    double LatencyReportIntervalMs,
    float HighLatencyThresholdMs,
    float LowLatencyThresholdMs,
    float SkipToLiveThresholdMs,
    double QualityDecisionIntervalMs,
    double QualityHysteresisWindowMs,
    int LatencyHistorySize,
    float PeerOutlierRatio,
    float PeerOutlierRatioSmallCall,
    // Baseline latency
    float BaselineLatencyRiseAbsoluteMs,
    float BaselineLatencyRiseMultiplier,
    float BaselineLatencyFastMarginMs,
    float BaselineLatencyEmaAlpha,
    // Root-cause classification
    float HighDecodeTimeThresholdMs,
    int HighBufferDepthThreshold,
    // Over-delivery detection
    float ThroughputOverDeliveryRatio,
    int ThroughputStepDownConsecutiveChecks,
    int LatencyStepDownConsecutiveChecks,
    // PLI rate limiting
    double KeyFrameRequestCooldownMs,
    // Warmup & codec switching
    double PeerWarmupDurationMs,
    double CodecSwitchHysteresisWindowMs,
    // Egress-side spatial fallback
    double EgressStallThresholdMs,
    double EgressRecoveryWindowMs,
    int EgressGapFrameThreshold,
    // Simulcast & stream management
    int MinMembersForSimulcast,
    int MaxWebcamStreamsPerChat,
    int PriorityActivationThreshold,
    double SilenceGracePeriodMs)
{
    public static VideoConstants Default { get; } = new(
        FrameRate: Constants.Video.FrameRate,
        FrameDurationMs: Constants.Video.FrameDuration.TotalMilliseconds,
        TargetBufferSize: Constants.Video.TargetBufferSize,
        TargetBufferDurationMs: Constants.Video.TargetBufferDuration.TotalMilliseconds,
        KeyFramePeriodMs: Constants.Video.KeyFramePeriod.TotalMilliseconds,
        KeyFramePeriodSize: Constants.Video.KeyFramePeriodSize,
        BufferHysteresisSize: Constants.Video.BufferHysteresisSize,
        MinBufferSize: Constants.Video.MinBufferSize,
        MaxBufferSize: Constants.Video.MaxBufferSize,
        ServerReplayTailDurationMs: Constants.Video.ServerReplayTailDuration.TotalMilliseconds,
        ServerReplayTailSize: Constants.Video.ServerReplayTailSize,
        CancellationDelayMs: Constants.Video.CancellationDelay.TotalMilliseconds,
        StreamExpirationDelayMs: Constants.Video.StreamExpirationDelay.TotalMilliseconds,
        MaxLiveDurationMs: Constants.Video.MaxLiveDuration.TotalMilliseconds,
        WebcamFrameSilenceTimeoutMs: Constants.Video.WebcamFrameSilenceTimeout.TotalMilliseconds,
        ScreencastFrameSilenceTimeoutMs: Constants.Video.ScreencastFrameSilenceTimeout.TotalMilliseconds,
        RpcStreamAckPeriod: Constants.Video.RpcStreamAckPeriod,
        RpcStreamBufferSize: Constants.Video.RpcStreamBufferSize,
        RetentionBufferSize: Constants.Video.RetentionBufferSize,
        ConsumerBufferSize: Constants.Video.ConsumerBufferSize,
        LatencyReportIntervalMs: Constants.Video.LatencyReportInterval.TotalMilliseconds,
        HighLatencyThresholdMs: Constants.Video.HighLatencyThresholdMs,
        LowLatencyThresholdMs: Constants.Video.LowLatencyThresholdMs,
        SkipToLiveThresholdMs: Constants.Video.SkipToLiveThresholdMs,
        QualityDecisionIntervalMs: Constants.Video.QualityDecisionInterval.TotalMilliseconds,
        QualityHysteresisWindowMs: Constants.Video.QualityHysteresisWindow.TotalMilliseconds,
        LatencyHistorySize: Constants.Video.LatencyHistorySize,
        PeerOutlierRatio: Constants.Video.PeerOutlierRatio,
        PeerOutlierRatioSmallCall: Constants.Video.PeerOutlierRatioSmallCall,
        BaselineLatencyRiseAbsoluteMs: Constants.Video.BaselineLatencyRiseAbsoluteMs,
        BaselineLatencyRiseMultiplier: Constants.Video.BaselineLatencyRiseMultiplier,
        BaselineLatencyFastMarginMs: Constants.Video.BaselineLatencyFastMarginMs,
        BaselineLatencyEmaAlpha: Constants.Video.BaselineLatencyEmaAlpha,
        HighDecodeTimeThresholdMs: Constants.Video.HighDecodeTimeThresholdMs,
        HighBufferDepthThreshold: Constants.Video.HighBufferDepthThreshold,
        ThroughputOverDeliveryRatio: Constants.Video.ThroughputOverDeliveryRatio,
        ThroughputStepDownConsecutiveChecks: Constants.Video.ThroughputStepDownConsecutiveChecks,
        LatencyStepDownConsecutiveChecks: Constants.Video.LatencyStepDownConsecutiveChecks,
        KeyFrameRequestCooldownMs: Constants.Video.KeyFrameRequestCooldown.TotalMilliseconds,
        PeerWarmupDurationMs: Constants.Video.PeerWarmupDuration.TotalMilliseconds,
        CodecSwitchHysteresisWindowMs: Constants.Video.CodecSwitchHysteresisWindow.TotalMilliseconds,
        EgressStallThresholdMs: Constants.Video.EgressStallThreshold.TotalMilliseconds,
        EgressRecoveryWindowMs: Constants.Video.EgressRecoveryWindow.TotalMilliseconds,
        EgressGapFrameThreshold: Constants.Video.EgressGapFrameThreshold,
        MinMembersForSimulcast: Constants.Video.MinMembersForSimulcast,
        MaxWebcamStreamsPerChat: Constants.Video.MaxWebcamStreamsPerChat,
        PriorityActivationThreshold: Constants.Video.PriorityActivationThreshold,
        SilenceGracePeriodMs: Constants.Video.SilenceGracePeriod.TotalMilliseconds);
}
