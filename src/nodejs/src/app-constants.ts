import { PromiseSource } from 'promises';

// Mirror of .NET `AppConstants` (single source of truth: `Constants.*`).
// Populated once at app startup via BrowserInit and propagated to every worker
// via its `init(...)` RPC. Reading `VIDEO.*` / `AUDIO.*` before init throws
// TypeError on undefined access — intentional, fail-loud behavior. `AC.appName`
// and `AC.prodHost` are usable from module-load time (skeletons need them).
// Code that needs the full snapshot should `await whenAppConstantsReady` first.

// Top-level snapshot. Holds per-pipeline sections as separate fields rather
// than flattening into the root. Stays JSON-serializable — runtime-only state
// like the init signal lives in the separate `whenAppConstantsReady` export.
export interface AppConstants {
    readonly appName: string;
    readonly prodHost: string;
    readonly video: VideoConstants;
    readonly audio: AudioConstants;
}

// VideoConstants: base fields come from .NET; derived fields (frame durations,
// buffer sizes, ms↔frame conversions) are computed in `initAppConstants`
// so we don't ship them over the wire.
export interface VideoConstants {
    // From .NET
    readonly frameRate: number;
    readonly targetBufferSize: number;
    readonly keyFramePeriodMs: number;
    readonly serverReplayTailDurationMs: number;
    readonly cancellationDelayMs: number;
    readonly streamExpirationDelayMs: number;
    readonly maxLiveDurationMs: number;
    readonly webcamFrameSilenceTimeoutMs: number;
    readonly screencastFrameSilenceTimeoutMs: number;
    readonly rpcStreamAckPeriod: number;
    readonly rpcStreamBufferSize: number;
    readonly latencyReportIntervalMs: number;
    readonly highLatencyThresholdMs: number;
    readonly lowLatencyThresholdMs: number;
    readonly skipToLiveThresholdMs: number;
    readonly qualityDecisionIntervalMs: number;
    readonly qualityHysteresisWindowMs: number;
    readonly latencyHistorySize: number;
    readonly peerOutlierRatio: number;
    readonly peerOutlierRatioSmallCall: number;
    readonly baselineLatencyRiseAbsoluteMs: number;
    readonly baselineLatencyRiseMultiplier: number;
    readonly baselineLatencyFastMarginMs: number;
    readonly baselineLatencyEmaAlpha: number;
    readonly highDecodeTimeThresholdMs: number;
    readonly highBufferDepthThreshold: number;
    readonly throughputOverDeliveryRatio: number;
    readonly throughputStepDownConsecutiveChecks: number;
    readonly latencyStepDownConsecutiveChecks: number;
    readonly keyFrameRequestCooldownMs: number;
    readonly peerWarmupDurationMs: number;
    readonly codecSwitchHysteresisWindowMs: number;
    readonly egressStallThresholdMs: number;
    readonly egressRecoveryWindowMs: number;
    readonly egressGapFrameThreshold: number;
    readonly minMembersForSimulcast: number;
    readonly maxWebcamStreamsPerChat: number;
    readonly priorityActivationThreshold: number;
    readonly silenceGracePeriodMs: number;
    // Derived in TS
    readonly frameDurationMs: number;            // 1000 / frameRate
    readonly targetBufferDurationMs: number;     // (targetBufferSize / frameRate) * 1000
    readonly keyFramePeriodSize: number;         // frameRate * keyFramePeriodMs / 1000
    readonly bufferHysteresisSize: number;       // floor(targetBufferSize / 2)
    readonly minBufferSize: number;              // targetBufferSize - bufferHysteresisSize
    readonly maxBufferSize: number;              // targetBufferSize + bufferHysteresisSize
    readonly serverReplayTailSize: number;       // frameRate * serverReplayTailDurationMs / 1000
}

// AudioConstants: base fields come from .NET (camelCased nested records);
// derived fields (sample counts, byte counts, second-unit aliases) are
// computed in `initAppConstants` so we don't ship them over the wire.
export interface AudioConstants {
    readonly rec: AudioRecConstants;
    readonly play: AudioPlayConstants;
    readonly encode: AudioEncodeConstants;
    readonly stream: AudioStreamConstants;
    readonly vad: AudioVadConstants;
}

export interface AudioRecConstants {
    // From .NET
    readonly sampleRate: number;
    readonly minRecordingGain: number;
    readonly minMicrophoneGain: number;
    readonly recordingInProgressReportPeriodMs: number;
    readonly heartbeat: AudioRecHeartbeatConstants;
    // Derived in TS
    readonly samplesPerMs: number;
    readonly sampleDuration: number; // seconds per sample
    readonly recordingInProgressReportSamples: number;
}

export interface AudioRecHeartbeatConstants {
    readonly intervalMs: number;
    readonly timeoutMs: number;
    readonly checkIntervalMs: number;
}

export interface AudioPlayConstants {
    // From .NET
    readonly sampleRate: number;
    readonly startBufferDurationMs: number;
    readonly startBufferGrowDurationMs: number;
    readonly startBufferDurationWithVideoMs: number;
    readonly lowBufferDurationMs: number;
    readonly stateUpdatePeriodMs: number;
    readonly mediaSessionResetDebounceMs: number;
    // Derived in TS
    readonly samplesPerMs: number;
    readonly samplesPerWindow: number;     // samples per encoder frame at playback rate
    readonly sampleDuration: number;       // seconds per sample
    readonly startBufferDuration: number;  // seconds
    readonly startBufferGrowDuration: number;
    readonly startBufferDurationWithVideo: number;
    readonly lowBufferDuration: number;
    readonly stateUpdatePeriod: number;
}

export interface AudioEncodeConstants {
    // From .NET
    readonly frameDurationMs: number;
    readonly bitrate: number;
    readonly channels: number;
    readonly fadeFrames: number;
    readonly maxBufferedFrames: number;
    readonly defaultPreSkip: number;
    // Derived in TS
    readonly byteRate: number;
    readonly frameSamples: number;
    readonly frameBytes: number;
    readonly frameBufferBytes: number;
}

export interface AudioStreamConstants {
    // From .NET
    readonly maxStreams: number;
    readonly delayFrames: number;
    readonly minPackFrames: number;
    readonly maxPackFrames: number;
    readonly maxBufferedFrames: number;
    readonly maxSpeed: number;
    readonly interStreamDelayMs: number;
    readonly streamErrorDelayMs: number;
    readonly connectErrorDelayMs: number;
    readonly debugRandomDisconnectPeriodMs: number;
    readonly rpcAckPeriod: number;
    readonly rpcBufferSize: number;
    // Derived in TS (seconds aliases)
    readonly interStreamDelay: number;
    readonly streamErrorDelay: number;
    readonly connectErrorDelay: number;
}

export interface AudioVadConstants {
    // From .NET
    readonly neuralFrameDurationMs: number;
    readonly webrtcFrameDurationMs: number;
    readonly apmFrameDurationMs: number;
    readonly minSpeechMs: number;
    readonly maxSpeechMs: number;
    readonly minSpeechToCancelPauseMs: number;
    readonly minPauseMs: number;
    readonly maxPauseMs: number;
    readonly maxConvPauseMs: number;
    readonly convDurationMs: number;
    readonly pauseVariesFromMs: number;
    readonly pauseVaryPower: number;
    readonly skipFirstRecordingMs: number;
    readonly skipSequentialCallsMs: number;
    readonly nnVadContextSamples: number;
    // Derived in TS
    readonly neuralFrameSamples: number;   // samples per neural-VAD window at rec rate
    readonly webrtcFrameSamples: number;   // samples per WebRTC-VAD window at rec rate
    // Seconds aliases
    readonly minSpeech: number;
    readonly maxSpeech: number;
    readonly minSpeechToCancelPause: number;
    readonly minPause: number;
    readonly maxPause: number;
    readonly maxConvPause: number;
    readonly convDuration: number;
    readonly pauseVariesFrom: number;
}

// Module-local mutable holders. Each ES module instance (main thread + each
// worker) has its own copy and must be init'd separately via initAppConstants.
// `AC` is seeded with `appName` and `prodHost` so module-load consumers
// (skeleton elements reading `AC.prodHost`) have valid values before
// `initAppConstants` runs. `VIDEO` / `AUDIO` are undefined! until init —
// accessing them before init throws TypeError naturally. Async consumers
// that need the full snapshot should `await whenAppConstantsReady`.
export let AC: AppConstants = {
    appName: 'Voxt', // Must be in sync with AppConstants value
    prodHost: 'voxt.ai', // Must be in sync with AppConstants value
} as AppConstants;
export let VIDEO: VideoConstants = undefined!;
export let AUDIO: AudioConstants = undefined!;

const _whenAppConstantsReady = new PromiseSource<void>();
export const whenAppConstantsReady: Promise<void> = _whenAppConstantsReady;
let initialized = false;

// First call wins. Subsequent calls are silently ignored so a redundant init
// from e.g. a re-acquired shared worker doesn't reset the holders.
export function initAppConstants(appConstants: AppConstants): void {
    if (initialized)
        return;

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (!appConstants?.video || !appConstants?.audio)
        throw new Error('Invalid app constants, "await whenAppConstantsReady" is missing?');

    AC = {
        ...appConstants,
        video: expandVideo(appConstants.video),
        audio: expandAudio(appConstants.audio),
    };
    VIDEO = AC.video;
    AUDIO = AC.audio;
    initialized = true;
    _whenAppConstantsReady.resolve(undefined);
}

// Computes derived video fields from the .NET-supplied base values.
// Kept module-private — consumers see the expanded `VideoConstants` shape via `VIDEO`.
function expandVideo(video: VideoConstants): VideoConstants {
    const { frameRate, targetBufferSize, keyFramePeriodMs, serverReplayTailDurationMs } = video;
    const bufferHysteresisSize = Math.floor(targetBufferSize / 2);
    return {
        ...video,
        frameDurationMs: 1000 / frameRate,
        targetBufferDurationMs: (targetBufferSize / frameRate) * 1000,
        keyFramePeriodSize: Math.round(frameRate * keyFramePeriodMs / 1000),
        bufferHysteresisSize,
        minBufferSize: targetBufferSize - bufferHysteresisSize,
        maxBufferSize: targetBufferSize + bufferHysteresisSize,
        serverReplayTailSize: Math.round(frameRate * serverReplayTailDurationMs / 1000),
    };
}

// Computes derived audio fields from the .NET-supplied base values.
// Kept module-private — consumers see the expanded `AudioConstants` shape via `AUDIO`.
function expandAudio(audio: AudioConstants): AudioConstants {
    const recSamplesPerMs = audio.rec.sampleRate / 1000;
    const playSamplesPerMs = audio.play.sampleRate / 1000;
    const frameDurationMs = audio.encode.frameDurationMs;
    const bitrate = audio.encode.bitrate;
    const frameBytes = Math.round(bitrate * frameDurationMs / 1000 / 8);
    return {
        rec: {
            ...audio.rec,
            samplesPerMs: recSamplesPerMs,
            sampleDuration: 0.001 / recSamplesPerMs,
            recordingInProgressReportSamples: recSamplesPerMs * audio.rec.recordingInProgressReportPeriodMs,
        },
        play: {
            ...audio.play,
            samplesPerMs: playSamplesPerMs,
            samplesPerWindow: playSamplesPerMs * frameDurationMs,
            sampleDuration: 0.001 / playSamplesPerMs,
            startBufferDuration: audio.play.startBufferDurationMs / 1000,
            startBufferGrowDuration: audio.play.startBufferGrowDurationMs / 1000,
            startBufferDurationWithVideo: audio.play.startBufferDurationWithVideoMs / 1000,
            lowBufferDuration: audio.play.lowBufferDurationMs / 1000,
            stateUpdatePeriod: audio.play.stateUpdatePeriodMs / 1000,
        },
        encode: {
            ...audio.encode,
            byteRate: Math.round(bitrate / 8),
            frameSamples: recSamplesPerMs * frameDurationMs,
            frameBytes,
            frameBufferBytes: 2 * frameBytes,
        },
        stream: {
            ...audio.stream,
            interStreamDelay: audio.stream.interStreamDelayMs / 1000,
            streamErrorDelay: audio.stream.streamErrorDelayMs / 1000,
            connectErrorDelay: audio.stream.connectErrorDelayMs / 1000,
        },
        vad: {
            ...audio.vad,
            neuralFrameSamples: recSamplesPerMs * audio.vad.neuralFrameDurationMs,
            webrtcFrameSamples: recSamplesPerMs * audio.vad.webrtcFrameDurationMs,
            minSpeech: audio.vad.minSpeechMs / 1000,
            maxSpeech: audio.vad.maxSpeechMs / 1000,
            minSpeechToCancelPause: audio.vad.minSpeechToCancelPauseMs / 1000,
            minPause: audio.vad.minPauseMs / 1000,
            maxPause: audio.vad.maxPauseMs / 1000,
            maxConvPause: audio.vad.maxConvPauseMs / 1000,
            convDuration: audio.vad.convDurationMs / 1000,
            pauseVariesFrom: audio.vad.pauseVariesFromMs / 1000,
        },
    };
}
