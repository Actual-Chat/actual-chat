// Mirror of .NET `AppConstants` (single source of truth: `Constants.*`).
// Populated once at app startup via BrowserInit and propagated to every worker
// via its `init(...)` RPC. Reading `APP_CONSTANTS.*` / `VIDEO.*` before init
// throws TypeError on undefined access — intentional, fail-loud behavior.

// Top-level snapshot. Currently holds only video constants; audio, presence,
// etc. join later as separate fields rather than flattening into the root.
export interface AppConstants {
    readonly video: VideoConstants;
}

export interface VideoConstants {
    // Frame rate / cadence
    readonly frameRate: number;
    readonly frameDurationMs: number;
    // Target playback buffer
    readonly targetBufferSize: number;
    readonly targetBufferDurationMs: number;
    // Keyframe cadence
    readonly keyFramePeriodMs: number;
    readonly keyFramePeriodSize: number;
    // Buffer hysteresis
    readonly bufferHysteresisSize: number;
    readonly minBufferSize: number;
    readonly maxBufferSize: number;
    // Server replay tail
    readonly serverReplayTailDurationMs: number;
    readonly serverReplayTailSize: number;
    // Stream lifecycle
    readonly cancellationDelayMs: number;
    readonly streamExpirationDelayMs: number;
    readonly maxLiveDurationMs: number;
    // Frame silence watchdogs
    readonly webcamFrameSilenceTimeoutMs: number;
    readonly screencastFrameSilenceTimeoutMs: number;
    // RPC stream flow control
    readonly rpcStreamAckPeriod: number;
    readonly rpcStreamBufferSize: number;
    readonly retentionBufferSize: number;
    readonly consumerBufferSize: number;
    // Latency & quality adaptation
    readonly latencyReportIntervalMs: number;
    readonly highLatencyThresholdMs: number;
    readonly lowLatencyThresholdMs: number;
    readonly skipToLiveThresholdMs: number;
    readonly qualityDecisionIntervalMs: number;
    readonly qualityHysteresisWindowMs: number;
    readonly latencyHistorySize: number;
    readonly peerOutlierRatio: number;
    readonly peerOutlierRatioSmallCall: number;
    // Baseline latency
    readonly baselineLatencyRiseAbsoluteMs: number;
    readonly baselineLatencyRiseMultiplier: number;
    readonly baselineLatencyFastMarginMs: number;
    readonly baselineLatencyEmaAlpha: number;
    // Root-cause classification
    readonly highDecodeTimeThresholdMs: number;
    readonly highBufferDepthThreshold: number;
    // Over-delivery detection
    readonly throughputOverDeliveryRatio: number;
    readonly throughputStepDownConsecutiveChecks: number;
    readonly latencyStepDownConsecutiveChecks: number;
    // PLI rate limiting
    readonly keyFrameRequestCooldownMs: number;
    // Warmup & codec switching
    readonly peerWarmupDurationMs: number;
    readonly codecSwitchHysteresisWindowMs: number;
    // Egress-side spatial fallback
    readonly egressStallThresholdMs: number;
    readonly egressRecoveryWindowMs: number;
    readonly egressGapFrameThreshold: number;
    // Simulcast & stream management
    readonly minMembersForSimulcast: number;
    readonly maxWebcamStreamsPerChat: number;
    readonly priorityActivationThreshold: number;
    readonly silenceGracePeriodMs: number;
}

// Module-local mutable holders. Each ES module instance (main thread + each
// worker) has its own copy and must be init'd separately via initAppConstants.
// Typed as defined; undefined at runtime until init runs — accessing
// APP_CONSTANTS.* / VIDEO.* before init throws TypeError naturally.
export let APP_CONSTANTS: AppConstants = undefined!;
export let VIDEO: VideoConstants = undefined!;
let initialized = false;

// First call wins. Subsequent calls are silently ignored so a redundant init
// from e.g. a re-acquired shared worker doesn't reset the holders.
export function initAppConstants(appConstants: AppConstants): void {
    if (initialized)
        return;

    APP_CONSTANTS = appConstants;
    VIDEO = appConstants.video;
    initialized = true;
}
