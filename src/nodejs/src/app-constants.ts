import { PromiseSource } from 'actuallab-core';

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
    readonly mobileFrameRate: number;
    readonly targetBufferSize: number;
    readonly keyFramePeriodMs: number;
    readonly serverReplayTailDurationMs: number;
    readonly cancellationDelayMs: number;
    readonly streamExpirationDelayMs: number;
    readonly maxLiveDurationMs: number;
    readonly cameraFrameSilenceTimeoutMs: number;
    readonly screenCastFrameSilenceTimeoutMs: number;
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
    readonly throughputStepDownConsecutiveChecks: number;
    readonly latencyStepDownConsecutiveChecks: number;
    readonly keyFrameRequestCooldownMs: number;
    readonly peerWarmupDurationMs: number;
    readonly codecSwitchHysteresisWindowMs: number;
    readonly egressStallThresholdMs: number;
    readonly egressRecoveryWindowMs: number;
    readonly egressGapFrameThreshold: number;
    readonly maxCameraStreamsPerChat: number;
    readonly priorityActivationThreshold: number;
    readonly silenceGracePeriodMs: number;
    readonly screenCastKeepAlivePeriodMs: number;
    readonly cameraLayerBaseBitratesKbps: readonly number[];
    readonly screenCastLayerBaseBitratesKbps: readonly number[];
    readonly codecDefs: readonly VideoCodecDef[];
    // Derived in TS
    readonly frameDurationMs: number;            // 1000 / frameRate
    readonly targetBufferSpanMs: number;         // (targetBufferSize / frameRate) * 1000
    readonly keyFramePeriodSize: number;         // frameRate * keyFramePeriodMs / 1000
    readonly bufferHysteresisSize: number;       // floor(targetBufferSize / 2)
    readonly minBufferSize: number;              // targetBufferSize - bufferHysteresisSize
    readonly maxBufferSize: number;              // targetBufferSize + bufferHysteresisSize
    readonly serverReplayTailSize: number;       // frameRate * serverReplayTailDurationMs / 1000
    readonly rpcStreamAckPeriod: number;         // floor(targetBufferSize / 3)
    readonly rpcStreamAckAdvance: number;        // targetBufferSize
    /**
     * RPC sender's local buffer (in source moments / bundles). Sized to
     * one keyframe interval × 1.33, so the sender's canSkipTo=isKeyFrame
     * compaction has room to skip past at least one full GOP without
     * wedging on a slow ACK. Fusion's pump fills this buffer
     * continuously while connected — capture drains into it regardless
     * of whether the wire window is open. Wired to RpcStreamOptions
     * `bufferSize` via {@link MediaRpcStreamOptions.videoRecording}.
     */
    readonly senderBufferSize: number;           // ceil(keyFramePeriodSize * 1.33)
    /**
     * push-to-pull-buffer (capture-side rendezvous Denque) capacity.
     * One source moment per slot. ≈ 1 s at frameRate. The flood gate
     * closes when this queue reaches half capacity and reopens at a
     * quarter — propagates backpressure to the capture source.
     */
    readonly pushPullBufferSize: number;         // frameRate
}

export enum VideoCodecKind {
    Unknown = 0,
    H264 = 1,
    Hevc = 2,
    Vp9 = 3,
    Av1 = 4,
}

export interface VideoCodecDef {
    readonly kind: VideoCodecKind;
    readonly efficiency: number;
}

export interface VideoCodecConstants {
    readonly codecDefs: readonly VideoCodecDef[];
}

// AudioConstants: base fields come from .NET (camelCased nested records);
// derived fields (sample counts, byte counts, second-unit aliases) are
// computed in `initAppConstants` so we don't ship them over the wire.
export interface AudioConstants {
    // Audio-wide cadence
    readonly frameRate: number;
    readonly rec: AudioRecConstants;
    readonly play: AudioPlayConstants;
    readonly encode: AudioEncodeConstants;
    readonly stream: AudioStreamConstants;
    readonly vad: AudioVadConstants;
    // Derived in TS
    readonly frameDurationMs: number;            // 1000 / frameRate
    readonly frameDurationTicks: number;         // .NET ticks per frame (10_000 per ms)
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
    readonly startBufferSize: number;
    readonly bufferHysteresisSize: number;
    readonly startBufferGrowDurationMs: number;
    readonly startBufferDurationWithVideoMs: number;
    readonly minEncodedBufferSizeMs: number;
    readonly decodedBufferSizeMs: number;
    readonly audioEnginePlaybackLatencyMs: number;
    readonly lowBufferDurationMs: number;
    readonly stateUpdatePeriodMs: number;
    readonly mediaSessionResetDebounceMs: number;
    readonly playbackHardSkipThresholdMs: number;
    readonly playbackMaxSpeedUpDurationMs: number;
    readonly playbackSpeedUpDropEveryNFrames: number;
    // Derived in TS
    readonly startBufferDurationMs: number;           // startBufferSize * 1000 / frameRate
    readonly minBufferSize: number;                   // startBufferSize - bufferHysteresisSize
    readonly samplesPerMs: number;
    readonly samplesPerWindow: number;                // samples per encoder frame at playback rate
    readonly sampleDuration: number;                  // seconds per sample
    readonly startBufferDuration: number;             // seconds
    readonly startBufferGrowDuration: number;
    readonly startBufferDurationWithVideo: number;
    readonly lowBufferDuration: number;
    readonly stateUpdatePeriod: number;
    readonly playbackHardSkipThreshold: number;       // seconds
    readonly playbackMaxSpeedUpDuration: number;      // seconds
}

export interface AudioEncodeConstants {
    // From .NET
    readonly bitrate: number;
    readonly channels: number;
    readonly fadeFrames: number;
    readonly maxBufferedFrames: number;
    readonly defaultPreSkip: number;
    readonly voicePreRollFrameLimit: number;
    // Derived in TS
    readonly byteRate: number;
    readonly frameSamples: number;
    readonly frameBytes: number;
    readonly frameBufferBytes: number;
}

export interface AudioStreamConstants {
    // From .NET
    readonly maxStreams: number;
    readonly minPackFrames: number;
    readonly maxPackFrames: number;
    readonly maxBufferedFrames: number;
    readonly maxSpeed: number;
    readonly interStreamDelayMs: number;
    readonly streamErrorDelayMs: number;
    readonly connectErrorDelayMs: number;
    readonly debugRandomDisconnectPeriodMs: number;
    readonly recordingRpcStreamAckPeriod: number;
    readonly deliveryRpcStreamAckPeriod: number;
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

export function parseVideoCodecKind(codec: string): VideoCodecKind {
    const c = codec.trim().toLowerCase();
    if (c.startsWith('avc1') || c.startsWith('h264')) return VideoCodecKind.H264;
    if (c.startsWith('hev1') || c.startsWith('hvc1') || c.startsWith('hevc') || c.startsWith('h265')) return VideoCodecKind.Hevc;
    if (c.startsWith('vp09') || c.startsWith('vp9')) return VideoCodecKind.Vp9;
    if (c.startsWith('av01') || c.startsWith('av1')) return VideoCodecKind.Av1;
    return VideoCodecKind.Unknown;
}

export function getVideoCodecEfficiency(codec: string, video: VideoCodecConstants = VIDEO): number {
    const kind = parseVideoCodecKind(codec);
    return video.codecDefs.find(x => x.kind === kind)?.efficiency
        ?? video.codecDefs.find(x => x.kind === VideoCodecKind.Unknown)?.efficiency
        ?? 1;
}

export function getVideoLayerBitrateKbps(
    baseBitrateKbps: number,
    codec: string,
    video: VideoCodecConstants = VIDEO,
): number {
    const efficiency = getVideoCodecEfficiency(codec, video);
    return Math.max(Number.EPSILON, baseBitrateKbps / efficiency);
}

export function getVideoLayerBitratesKbps(
    baseBitratesKbps: readonly number[],
    codec: string,
    video: VideoCodecConstants = VIDEO,
): number[] {
    return baseBitratesKbps.map(x => getVideoLayerBitrateKbps(x, codec, video));
}

export function getVideoLayerByteRate(
    baseBitrateKbps: number,
    codec: string,
    video: VideoCodecConstants = VIDEO,
): number {
    return Math.max(1, Math.ceil(getVideoLayerBitrateKbps(baseBitrateKbps, codec, video) * 1_000 / 8));
}

export function kbpsToBitsPerSecond(bitrateKbps: number): number {
    return Math.max(1, Math.round(bitrateKbps * 1_000));
}

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
    const keyFramePeriodSize = Math.round(frameRate * keyFramePeriodMs / 1000);
    return {
        ...video,
        frameDurationMs: 1000 / frameRate,
        targetBufferSpanMs: (targetBufferSize / frameRate) * 1000,
        keyFramePeriodSize,
        bufferHysteresisSize,
        minBufferSize: targetBufferSize - bufferHysteresisSize,
        maxBufferSize: targetBufferSize + bufferHysteresisSize,
        serverReplayTailSize: Math.round(frameRate * serverReplayTailDurationMs / 1000),
        rpcStreamAckPeriod: Math.floor(targetBufferSize / 3),
        rpcStreamAckAdvance: targetBufferSize,
        senderBufferSize: Math.ceil(keyFramePeriodSize * 4 / 3),
        pushPullBufferSize: frameRate,
    };
}

// Computes derived audio fields from the .NET-supplied base values.
// Kept module-private — consumers see the expanded `AudioConstants` shape via `AUDIO`.
function expandAudio(audio: AudioConstants): AudioConstants {
    const frameRate = audio.frameRate;
    const frameDurationMs = 1000 / frameRate;
    const recSamplesPerMs = audio.rec.sampleRate / 1000;
    const playSamplesPerMs = audio.play.sampleRate / 1000;
    const bitrate = audio.encode.bitrate;
    const frameBytes = Math.round(bitrate * frameDurationMs / 1000 / 8);
    const startBufferDurationMs = audio.play.startBufferSize * 1000 / frameRate;
    return {
        ...audio,
        frameDurationMs,
        frameDurationTicks: frameDurationMs * 10_000,
        rec: {
            ...audio.rec,
            samplesPerMs: recSamplesPerMs,
            sampleDuration: 0.001 / recSamplesPerMs,
            recordingInProgressReportSamples: recSamplesPerMs * audio.rec.recordingInProgressReportPeriodMs,
        },
        play: {
            ...audio.play,
            startBufferDurationMs,
            minBufferSize: audio.play.startBufferSize - audio.play.bufferHysteresisSize,
            samplesPerMs: playSamplesPerMs,
            samplesPerWindow: playSamplesPerMs * frameDurationMs,
            sampleDuration: 0.001 / playSamplesPerMs,
            startBufferDuration: startBufferDurationMs / 1000,
            startBufferGrowDuration: audio.play.startBufferGrowDurationMs / 1000,
            startBufferDurationWithVideo: audio.play.startBufferDurationWithVideoMs / 1000,
            lowBufferDuration: audio.play.lowBufferDurationMs / 1000,
            stateUpdatePeriod: audio.play.stateUpdatePeriodMs / 1000,
            playbackHardSkipThreshold: audio.play.playbackHardSkipThresholdMs / 1000,
            playbackMaxSpeedUpDuration: audio.play.playbackMaxSpeedUpDurationMs / 1000,
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
