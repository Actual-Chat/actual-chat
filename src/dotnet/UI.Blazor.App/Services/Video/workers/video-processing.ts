/**
 * Video processing implementation.
 * Core logic for segmentation, encoding, and streaming — used by video-processing-worker.ts.
 *
 * NOTE: image segmentation (via onnxruntime-web) is intentionally disabled in
 * this release. The dead/unused-symbol warnings come from the segmentation
 * code paths that are commented out below — re-enable along with the
 * imports and function bodies when segmentation ships.
 */

/* eslint-disable @typescript-eslint/no-unused-vars, prefer-const */

import { rpcNoWait } from 'rpc';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';
// ONNX runtime is currently disabled in the video pipeline — image segmentation
// is implemented but not yet wired into any UI flow, and onnxruntime-web is
// heavy on low-end mobiles. Re-enable these imports (and the segmentation
// function bodies below) when segmentation ships in a future release.
// import * as ort from 'onnxruntime-web';
import { initAppConstants, VIDEO } from 'app-constants';

import { type EncoderConfig, type EncodedChunkData, WebCodecsEncoder } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats, ModelConfig, SpatialLayerConfig, VideoProcessingConfig, VideoProcessingWorker, VideoProcessingWorkerCallbacks, VideoProcessingStats, VideoProcessingStreamingStats, OrientationStats } from './video-processing-worker-contract';
import { getModelConfig } from './video-processing-worker-contract';
// import {
//     initTensorWebGPU,
//     processDeferredCleanups,
//     returnPooledBuffer,
//     videoFrameToTensorFloat32,
//     videoFrameToTensorUint8,
// } from '../tensor-utils';
import {
    applyBackgroundBlur,
    submitBlurI420,
    awaitAllPendingReadbacks,
    applyTemporalSmoothing,
    initBlurWebGPU,
    processBlurDeferredCleanups,
} from '../webgpu-blur';
import { WebGPUManager } from '../webgpu-manager';
import { WebGpuDownscaler, type DownscaleTarget } from '../webgpu-downscaler';

import { isAvcCDescription, deriveAvcCodecFromDescription, pickAvcLevelByte, resizeFrame, cpuRgbaToI420 } from './video-encoding-helpers';
import { getCodecCategory } from '../codec-support';
import {
    type VideoStreamFrame, type StreamingContext,
    microsecondsToTicks, InternalVideoStream, serverClockNow,
} from './video-streaming';
import { Api } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import { ReplaceableSlot } from 'buffers';

// Import the ONNX model so esbuild copies it to dist/assets/onnx/
// Disabled with the rest of segmentation — re-enable when shipping.
// import SegmentationModelUrl from './selfie_segmentation_olive_webgpu.onnx';

// Type declarations for Insertable Streams API (may be available in worker scope on Safari 18+)
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}
// Structural ctor types for the two output-track flavours: standardized
// `VideoTrackGenerator` (Safari 18+) and Chromium's older proprietary
// `MediaStreamTrackGenerator`. Both produce a writable VideoFrame stream + a
// readable MediaStreamTrack. We probe `self` for these names at runtime.
type VideoTrackGeneratorCtor = new () => { writable: WritableStream<VideoFrame>; track: MediaStreamTrack };
type MediaStreamTrackGeneratorCtor = new (opts: { kind: 'video' }) => { writable: WritableStream<VideoFrame>; track: MediaStreamTrack };

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

// ─── Callbacks (set by worker entry after RPC init) ─────────────────────────

let callbacks: VideoProcessingWorkerCallbacks;

export function setCallbacks(cb: VideoProcessingWorkerCallbacks): void {
    callbacks = cb;
}

// ─── State ──────────────────────────────────────────────────────────────────

// Encoder. `encoder` is the base layer (SpatialLayerId=0) and is always present
// when encoding is active. `extraLayerEncoders` holds simulcast layers 1..N when
// VideoProcessingConfig.spatialLayers is set, each tagged with its SpatialLayerId
// via the WebCodecsEncoder ctor. Empty for single-encoder (P2P) mode.
let encoder: WebCodecsEncoder | null = null;
let extraLayerEncoders: WebCodecsEncoder[] = [];
let encoderConfig: EncoderConfig | null = null;

// Encoder pool — survives across stop/start cycles to keep the NVENC session
// slot held during the gap. Without this, a fresh `VideoEncoder.configure()`
// after stop collides with the previous session's still-releasing handle and
// fails with `OperationError: Encoder initialization error`. Pool is purged
// after POOL_TTL_MS of idle to avoid retaining HW indefinitely.
interface PrimaryPoolEntry {
    encoder: WebCodecsEncoder;
    codec: string;
    width: number;
    height: number;
    // Cached HVCC/AVCC description from the pooled encoder's first keyframe.
    // Chrome's VideoEncoder.configure() on an already-configured encoder is a
    // reconfigure and does not re-emit the description on subsequent keyframes,
    // so without this stash the next session can't build the stream's
    // codecSettings and frames pile up unsent. Same NVENC session → same bytes.
    description: Uint8Array | null;
}
let pooledPrimary: PrimaryPoolEntry | null = null;
let poolExpireTimer: ReturnType<typeof setTimeout> | null = null;
const POOL_TTL_MS = 30_000;
// True once .initialize() (configure) has been called on `encoder` and any
// extras. Encoder.configure() is deferred to the first encoded frame so the
// configure call sees the FINAL dims (after first-frame rotation/dim reconcile)
// and avoids the configure→reconfigure flip-flop that crashes Chrome HW HEVC.
let encodersInitialized = false;
let encoderFailed = false;
let encoderErrorSeen = false;
let framesWithoutOutput = 0;
let nextFrameIsKeyFrame = false;

// Same-codec rebuild policy: on the first async OperationError we try to
// resurrect the encoder once before declaring the codec dead. Many failures
// are transient (MediaCodec reconfigure race, NVENC slot contention) and a
// fresh VideoEncoder instance recovers cleanly. If the rebuild itself fails
// or another error arrives within the cooldown, we fall through to the
// standard codec-fallback chain.
let encoderRebuildAttempted = false;
let encoderRebuildInFlight = false;
let lastEncoderRebuildAtMs = 0;
const ENCODER_REBUILD_COOLDOWN_MS = 5000;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let downscaler: WebGpuDownscaler | null = null;
let senderRotationDeg = 0;
let startTimestamp: number | undefined = undefined;
let sourceStartedAtMs: number | undefined = undefined;
let loggedI420Error = false;
let loggedPreConvertSkipped = false;
let loggedPreviewCloneError = false;

// Replaceable slot ahead of the encoder (target design: "video encoder"
// stage holds at most one pending frame; newer raw frame replaces an older
// pending one when the encoder is still busy). See docs/video-pipeline.md.
const pendingEncoderFrame = new ReplaceableSlot<VideoFrame>({
    dispose: frame => {
        try { frame.close(); } catch { /* already closed */ }
    },
});
// "Encoder is busy" boundary used for slot decisions. Doc-pure 0 would idle
// the encoder between 33ms frames at 30fps; iOS HW encoders are fragile so
// a tighter threshold is used there.
const ENCODER_BUSY_THRESHOLD = DeviceInfo.isIos ? 1 : 3;

// Slot-replacement observability — counts arrivals where the slot was
// already occupied (i.e. encoder couldn't keep up). Surfaced over a 5 s
// window via onBackpressure(rate) so the main thread can react (e.g. drop
// a simulcast layer or step bitrate down).
let slotReplacements = 0;
let slotArrivals = 0;
let lastSlotCheckTime = 0;
const slotWindowMs = 5000;
const slotReplaceThreshold = 0.20;
let slotPressureNotified = false;

// Recorder-health 1 Hz aggregator (Step 9.2). Surfaces max per-frame encode
// cost across spatial layers, slot pressure, RpcStreamSender dropped-frame
// count, and ACK signals to .NET via `callbacks.onRecorderHealthSnapshot`.
// VideoQualityUI's recording branch consumes the snapshot to drive simulcast
// layer decisions.
const recorderHealthIntervalMs = 1000;
const recorderHealthFrameBudgetMs = 1000 / 30;
const encodeCostRatioSamples: number[] = [];
const pendingEncodeFrameCosts = new Map<number, PendingEncodeFrameCost>();
let lastSenderTotalDroppedFrames = 0;
let lastSenderAckAtMs = 0;
let recorderHealthIntervalHandle: ReturnType<typeof setInterval> | null = null;
let recorderHealthSenderHooked: object | null = null;
// Wall-clock time of the most recent recorder-health metrics reset. Reset
// is triggered when the encoder pipeline shape changes — encoder replace,
// codec switch, simulcast ladder reshape, recording start. The first 1–2
// frames produced after such a reset are dominated by encoder warmup (cold
// IDR, codec init, NVENC slot adoption, fresh extra-layer cold start) and
// their encode-time samples spike to many frame durations. Without a settle
// window, the AIMD aggregator immediately reverts the climb to N+1 layers
// and oscillates indefinitely between N and N+1 tier ladders. During the
// settle window the aggregator emits 0/0 ratios so the classifier produces
// neutral signals.
let lastRecorderHealthResetAtMs = 0;
const RECORDER_HEALTH_RESET_SETTLE_MS = 3000;
interface PendingEncodeFrameCost {
    expectedLayerCount: number;
    seenLayerIds: Set<number>;
    maxEncodeTimeMs: number;
    startedAtMs: number;
}

// Segmentation
// ort.InferenceSession / ort.Tensor types replaced with `unknown` while
// onnxruntime-web is disabled. Restore the original types when re-enabling.
let onnxSession: unknown = null;
let segConfig: SegmentationConfig | null = null;
let resolvedModelConfig: ModelConfig | null = null;
let outputGpuBuffer: GPUBuffer = null!;
let outputTensor: unknown = null;
let smoothedMaskBuffer: GPUBuffer = null!;
let blurEnabled = false;
let segInitialized = false;

let segProcessedFrames = 0;
let segTotalInferenceTime = 0;
let segTotalBlurTime = 0;
let segTotalProcessingTime = 0;
let segDroppedFrames = 0;
let segFrameCounter = 0;
let hasValidMask = false;

interface QueuedFrame { frame: VideoFrame; sequenceNumber: number; timestamp: number }
function startRecorderHealthAggregator(): void {
    if (recorderHealthIntervalHandle) return;
    lastSenderTotalDroppedFrames = 0;
    lastSenderAckAtMs = 0;
    recorderHealthSenderHooked = null;
    // Reset the metrics on aggregator start so the first cold-start ticks
    // (codec configure + first IDR + simulcast extras coming online) don't
    // pollute the encode-time distribution.
    resetRecorderHealthMetrics();
    recorderHealthIntervalHandle = setInterval(emitRecorderHealthSnapshot, recorderHealthIntervalMs);
}

// Discards accumulated encode-cost samples and the pending per-frame cost
// trackers, and arms a short settle window during which the aggregator
// reports neutral (0/0) ratios. Called on encoder replace, codec switch,
// simulcast ladder reshape, and recording start — events that produce real
// encoder warmup activity but no sustained overload. Without the reset, the
// freshly-added encoder's cold-start spikes (driver/HW init on first encode
// submission) would be classified as overload and trigger a pointless
// AIMD backoff.
function resetRecorderHealthMetrics(): void {
    lastRecorderHealthResetAtMs = performance.now();
    encodeCostRatioSamples.length = 0;
    pendingEncodeFrameCosts.clear();
}

function stopRecorderHealthAggregator(): void {
    if (!recorderHealthIntervalHandle) return;
    clearInterval(recorderHealthIntervalHandle);
    recorderHealthIntervalHandle = null;
    encodeCostRatioSamples.length = 0;
    pendingEncodeFrameCosts.clear();
    recorderHealthSenderHooked = null;
}

function emitRecorderHealthSnapshot(): void {
    const now = performance.now();
    flushStaleEncodeFrameCosts(now);
    const inResetSettleWindow = lastRecorderHealthResetAtMs > 0
        && now - lastRecorderHealthResetAtMs < RECORDER_HEALTH_RESET_SETTLE_MS;
    const samples = encodeCostRatioSamples.slice();
    encodeCostRatioSamples.length = 0;
    samples.sort((a, b) => a - b);
    const pickAt = (q: number): number => {
        if (samples.length === 0) return 0;
        const idx = Math.min(Math.floor(samples.length * q), samples.length - 1);
        return samples[idx] ?? 0;
    };
    // While the post-reset settle window is open, emit 0/0 so the .NET
    // classifier returns a neutral signal — neither a backoff nor a climb
    // candidate. Without this gate, a freshly-added top simulcast tier's
    // cold-start spikes (driver/HW init on first encode call) would falsely
    // classify as overload and cause endless N⇌N+1 oscillation.
    const encodeRatioAvg = inResetSettleWindow || samples.length === 0
        ? 0
        : samples.reduce((sum, x) => sum + x, 0) / samples.length;
    const encodeRatioP90 = inResetSettleWindow ? 0 : pickAt(0.9);

    const slotRate = slotReplacements / Math.max(1, slotArrivals);

    const sender = videoStream?.senderStats ?? null;
    if (sender && recorderHealthSenderHooked !== sender) {
        sender.onAckProcessed = () => { lastSenderAckAtMs = performance.now(); };
        recorderHealthSenderHooked = sender;
    }
    const senderTotalDroppedFrames = sender?.totalSkipped ?? 0;
    const senderDroppedFramesPerSecond = Math.max(
        0,
        senderTotalDroppedFrames - lastSenderTotalDroppedFrames);
    lastSenderTotalDroppedFrames = senderTotalDroppedFrames;
    const senderFrameDropRatio = senderDroppedFramesPerSecond / Math.max(1, VIDEO.frameRate);

    const lastAckAgeMs = lastSenderAckAtMs > 0 ? now - lastSenderAckAtMs : -1;

    void callbacks.onRecorderHealthSnapshot(
        encodeRatioAvg, encodeRatioP90,
        slotRate, senderFrameDropRatio,
        lastAckAgeMs, WorkerConnectivityUI.isConnected, rpcNoWait);
}

function registerEncodeFrameCost(timestamp: number, expectedLayerCount: number): void {
    if (!recorderHealthIntervalHandle)
        return;

    pendingEncodeFrameCosts.set(timestamp, {
        expectedLayerCount: Math.max(1, expectedLayerCount),
        seenLayerIds: new Set<number>(),
        maxEncodeTimeMs: 0,
        startedAtMs: performance.now(),
    });
}

function recordEncodeLayerCost(timestamp: number, spatialLayerId: number, encodeTimeMs: number): void {
    if (!recorderHealthIntervalHandle)
        return;

    const frameCost = pendingEncodeFrameCosts.get(timestamp);
    if (!frameCost)
        return;

    frameCost.seenLayerIds.add(spatialLayerId);
    frameCost.maxEncodeTimeMs = Math.max(frameCost.maxEncodeTimeMs, encodeTimeMs);
    if (frameCost.seenLayerIds.size >= frameCost.expectedLayerCount)
        completeEncodeFrameCost(timestamp, frameCost);
}

function flushStaleEncodeFrameCosts(nowMs: number): void {
    for (const [timestamp, frameCost] of pendingEncodeFrameCosts) {
        if (nowMs - frameCost.startedAtMs >= recorderHealthIntervalMs)
            completeEncodeFrameCost(timestamp, frameCost);
    }
}

function completeEncodeFrameCost(timestamp: number, frameCost: PendingEncodeFrameCost): void {
    pendingEncodeFrameCosts.delete(timestamp);
    if (frameCost.seenLayerIds.size === 0)
        return;

    encodeCostRatioSamples.push(frameCost.maxEncodeTimeMs / recorderHealthFrameBudgetMs);
}

// Replaceable slot ahead of segmentation/blur (target design: "raw video
// processors" stage uses a size-1 slot, newer raw frame replaces a pending
// one). See docs/video-pipeline.md.
const pendingFrame = new ReplaceableSlot<QueuedFrame>({
    dispose: qf => {
        try { qf.frame.close(); } catch { /* already closed */ }
    },
});
let processingFrame = false;
let frameSequence = 0;

// Streaming
const streamCtx: StreamingContext = {
    chatId: '',
    serverClockOffsetMs: 0,
    streamKind: 0,
    processing: false,
    apiUrl: null,
    sessionTokenProvider: undefined,
    rpcLiveVideoStreams: null,
};
let videoStream: InternalVideoStream | null = null;
let lastVideoStream: InternalVideoStream | null = null;

/**
 * Apply a `VideoProcessingConfig.streaming` block to the shared `streamCtx`.
 * The Fusion RPC peer is constructed lazily on the first frame by
 * `InternalVideoStream` / `ensureRpcPush(ctx)`, so this is a pure state copy.
 */
function applyStreamingConfig(config: VideoProcessingConfig): void {
    const s = config.streaming;
    streamCtx.chatId = s.chatId;
    streamCtx.serverClockOffsetMs = s.serverClockOffsetMs;
    streamCtx.streamKind = s.streamKind ?? 0;
    streamCtx.apiUrl = s.apiUrl;
    streamCtx.sessionTokenProvider = minLifespanMs => callbacks.getSessionToken(minLifespanMs);
    streamingEnabled = true;
    streamStatus = 'waiting for first frame';
    streamProgressAt = 0;
    streamingStallNotified = false;
    startStreamingWatchdog();
    // Declare our intent to keep a connection up while we capture.
    // Actual attempts are still gated by `Api.isDotNetRpcConnected`.
    Api.requireConnection('VideoCapture');

    if (!streamCtx.apiUrl)
        warnLog?.log('streaming enabled but apiUrl is empty — push will fail at stream creation');
}

function startStreamingWatchdog(): void {
    if (streamingWatchdogTimer !== null) return;
    streamingWatchdogTimer = setInterval(checkStreamingStall, STREAMING_WATCHDOG_INTERVAL_MS);
}

function stopStreamingWatchdog(): void {
    if (streamingWatchdogTimer === null) return;
    clearInterval(streamingWatchdogTimer);
    streamingWatchdogTimer = null;
}

function checkStreamingStall(): void {
    if (streamingStallNotified) return;
    if (!streamingEnabled || !processing) return;
    // Encoder failures have their own surfacing path — let it own the UI.
    if (encoderFailed) return;
    // 'streaming' means frames are flowing onto the wire; refresh the
    // progress timestamp and bail. Same for the brief codec-switch window
    // (encoder torn down on purpose, will resume after the switch).
    if (streamStatus === 'streaming') {
        streamProgressAt = performance.now();
        return;
    }
    if (switchInProgress) return;
    // Encoder hasn't produced anything yet — nothing to stall on.
    if (streamProgressAt === 0) return;
    // Connection-driven outages have a separate UI surface. Skip while
    // offline/disconnected, and give the just-reconnected case a grace
    // window so the new keyframe + stream re-creation can complete before
    // we accuse the pipeline of being stuck.
    if (!WorkerConnectivityUI.isOnline) return;
    if (!WorkerConnectivityUI.isConnected) return;
    if (WorkerConnectivityUI.justBecameConnected(STREAMING_RECONNECT_GRACE_MS)) return;

    const stallMs = performance.now() - streamProgressAt;
    if (stallMs < STREAMING_STALL_TIMEOUT_MS) return;

    streamingStallNotified = true;
    stopStreamingWatchdog();
    const reason = `Video isn't reaching viewers (stalled in '${streamStatus}'). Try toggling the camera off and on.`;
    warnLog?.log(`Streaming watchdog: stall after ${stallMs.toFixed(0)}ms in '${streamStatus}', lastStreamError='${lastStreamError}' — notifying main thread`);
    void callbacks.onStreamingStalled(reason, rpcNoWait);
}
let pendingStreamFrames: VideoStreamFrame[] = [];
let codecSettings: string | null = null;
const storedDescriptionBytesByLayer = new Map<number, Uint8Array>();
let streamingEnabled = false;
let streamRecreations = 0;
let streamStatus = 'idle';
let lastStreamError = '';

// ─── Streaming stall watchdog ───────────────────────────────────────────────
// Fires `callbacks.onStreamingStalled` once when the encoder is producing
// chunks but they aren't reaching the wire (stream creation stuck on a
// missing codec description, peer-change stream not recovering, etc.). The
// connectivity layer has its own UI for "we lost the connection" — that
// case is filtered out here so we don't double-report it.
let streamProgressAt = 0;
let streamingStallNotified = false;
let streamingWatchdogTimer: ReturnType<typeof setInterval> | null = null;
const STREAMING_STALL_TIMEOUT_MS = 10_000;
const STREAMING_WATCHDOG_INTERVAL_MS = 2_000;
const STREAMING_RECONNECT_GRACE_MS = 20_000;
// Set true while a codec switch is tearing down the encoder and rebuilding it.
// While set, the frame pump must drop incoming frames instead of feeding them
// to a closed/transitioning encoder — every encode() call against a closed
// encoder fires onError, which used to flood the log path and freeze the UI.
let switchInProgress = false;

// ─── Screencast heartbeat ──────────────────────────────────────────────────
// getDisplayMedia is change-driven: a static screen produces zero frames. Without
// traffic, (a) the server's frame-silence watchdog reaps the stream, and (b) the
// receiver's SKIP_TO_LIVE check (3s threshold) trips and force-reloads, both
// pointlessly on unchanged content. We re-encode the cached last frame every
// SCREENCAST_HEARTBEAT_MS — interval sits well below the 3s receiver threshold
// so receiver latency stays bounded. Keyframes are auto-promoted by the encoder
// via maxKeyFrameIntervalMs=2000 (see recording-service.ts), so we don't force
// one here; encoder emits tiny P-frames for identical content most of the time
// (a few hundred bytes) and a real keyframe every ~2s for mid-stream joiners.
// Webcam skips this — its sensor emits continuously, so the path is dead code
// for streamKind=0.
const SCREENCAST_HEARTBEAT_MS = 1_000;
const SCREENCAST_HEARTBEAT_CHECK_MS = 500;
let lastEncodedFrame: VideoFrame | null = null;
let lastEncodedFrameAt = 0; // performance.now() of the last encoder.encode() we drove
let lastEncodedTimestampUs = 0; // μs timestamp of the last frame fed to encoder (real or heartbeat)
let heartbeatTimer: ReturnType<typeof setInterval> | null = null;

// Pipeline
let processing = false;
let dimensionsReconciled = false;
let needsRotation = false;
let orientationStats: OrientationStats | null = null;
// Native source dimensions as they arrive from MSTP (before any downscale). Sent to
// server on PushVideo so it knows the upscaling headroom — e.g. a screencast encoder
// configured at 1080p can be asked to step up to 4K only if the source is actually 4K.
let sourceWidth = 0;
let sourceHeight = 0;
let vadSpeaking = true;
let vadRemoteStreamCount = 0;
let vadReducedFrameIntervalMs = 1000 / 5;
let vadLastPassedFrameTime = 0;
let streamReadLoopPromise: Promise<void> | null = null;

// Preview-track output (MSTG path). When set, processed frames are written to
// `previewWriter` instead of being copied across the RPC boundary as VideoFrames.
let previewWriter: WritableStreamDefaultWriter<VideoFrame> | null = null;
let previewWriterClosed = false;

// Input track held by `startWithTrack` / `startPreviewWithTrack` (MSTP source).
// Tracked here so `stop()` can explicitly release it instead of relying on GC
// of the MSTP processor — keeps the camera light from lingering.
let inputTrack: MediaStreamTrack | null = null;

// ─── Segmentation ───────────────────────────────────────────────────────────

function initializeQueue(_cfg: SegmentationConfig): void {
    pendingFrame.clear();
    infoLog?.log('Segmentation pending-frame slot initialized');
}

function enqueueFrame(frame: VideoFrame): void {
    if (!segConfig) {
        void encodeProcessedFrame(frame);
        return;
    }

    const queuedFrame: QueuedFrame = {
        frame,
        sequenceNumber: frameSequence++,
        timestamp: performance.now(),
    };

    if (pendingFrame.hasValue) {
        debugLog?.log(`Replacing pending frame #${pendingFrame.value!.sequenceNumber} (slot occupied)`);
        segDroppedFrames++;
    }
    pendingFrame.push(queuedFrame);
    if (!processingFrame) void processPending();
}

function emitPreviewAndEncode(frame: VideoFrame): void {
    if (encoder) {
        // Streaming mode: encoder consumes the frame; preview is emitted from
        // *inside* `encodeProcessedFrame` after rotation+downscale so what the
        // remote peer sees is what the local preview shows (WYSIWYG).
        void encodeProcessedFrame(frame);
    } else {
        // Preview-only mode: no encoder/downscale step — emit the post-blur
        // frame directly. (Optional MSTG path still applies; falls back to RPC.)
        emitPreview(frame);
    }
}

// Single point that delivers a processed VideoFrame to the preview consumer.
// MSTG path (`previewWriter` set) writes to the generator's WritableStream — the
// browser closes the frame after the writer takes ownership. RPC path (legacy
// fallback) hands the frame to `onPreviewFrame`, which serialises it across.
function emitPreview(frame: VideoFrame): void {
    if (previewWriter && !previewWriterClosed) {
        previewWriter.write(frame).catch((err: unknown) => {
            warnLog?.log('Preview MSTG write failed:', err);
            previewWriterClosed = true;
            try { frame.close(); } catch { /* already closed */ }
        });
    } else {
        void callbacks.onPreviewFrame(frame, rpcNoWait);
    }
}

// eslint-disable-next-line @typescript-eslint/require-await
async function processPending(): Promise<void> {
    // Segmentation disabled in this release — see file header. The body below
    // is preserved for future re-enablement; while disabled, processPending is
    // never reached because `segInitialized` stays false (callers gate on it).
    return;

    /*
    if (!segConfig || processingFrame) return;
    processingFrame = true;

    try {
        while (pendingFrame.hasValue && processing) {
            const qf = pendingFrame.take()!;
            segFrameCounter++;

            processDeferredCleanups();
            processBlurDeferredCleanups();

            const frameSkipInterval = segConfig.frameSkipInterval ?? 1;
            if (frameSkipInterval > 1 && segFrameCounter % frameSkipInterval !== 0 && hasValidMask) {
                const skipBlurStart = performance.now();
                if (segConfig.blurEnabled) {
                    try {
                        await submitBlurI420(
                            qf.frame, smoothedMaskBuffer,
                            segConfig.inputWidth, segConfig.inputHeight,
                            { blurStrength: segConfig.blurRadius, maskDirty: false,
                                outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight },
                            (result) => {
                                segProcessedFrames++;
                                emitPreviewAndEncode(result.frame);
                            }
                        );
                    } catch {
                        const finalFrame = applyBackgroundBlur(
                            qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                            { blurStrength: segConfig.blurRadius, maskDirty: false,
                                outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight }
                        );
                        segProcessedFrames++;
                        emitPreviewAndEncode(finalFrame);
                    }
                } else {
                    segProcessedFrames++;
                    void encodeProcessedFrame(qf.frame);
                }
                segTotalBlurTime += performance.now() - skipBlurStart;
                segTotalProcessingTime += performance.now() - qf.timestamp;
                continue;
            }

            // Full inference
            const inferenceStartTime = performance.now();

            let inputTensor: ort.Tensor;
            if (resolvedModelConfig!.tensorFormat === 'nchw_float32') {
                inputTensor = await videoFrameToTensorFloat32(qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            } else {
                inputTensor = await videoFrameToTensorUint8(qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            }

            const outputName = onnxSession!.outputNames[0];
            await onnxSession!.run({ [onnxSession!.inputNames[0]]: inputTensor }, { [outputName]: outputTensor });

            const gpuBuffer = outputTensor.gpuBuffer;
            hasValidMask = true;
            returnPooledBuffer(inputTensor.gpuBuffer);

            const inferenceTime = performance.now() - inferenceStartTime;
            const blurStartTime = performance.now();

            if (segConfig.blurEnabled) {
                const smoothingAlpha = segConfig.temporalSmoothingFactor ?? 0.3;
                try {
                    await submitBlurI420(
                        qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                        { blurStrength: segConfig.blurRadius, smoothingSource: gpuBuffer, smoothingAlpha,
                            outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight },
                        (result) => {
                            segProcessedFrames++;
                            emitPreviewAndEncode(result.frame);
                        }
                    );
                } catch {
                    const finalFrame = applyBackgroundBlur(
                        qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                        { blurStrength: segConfig.blurRadius, smoothingSource: gpuBuffer, smoothingAlpha,
                            outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight }
                    );
                    segProcessedFrames++;
                    emitPreviewAndEncode(finalFrame);
                }
            } else {
                const smoothingAlpha = segConfig.temporalSmoothingFactor ?? 0.3;
                const maskSize = segConfig.inputWidth * segConfig.inputHeight;
                applyTemporalSmoothing(gpuBuffer, smoothedMaskBuffer, maskSize, smoothingAlpha);
                segProcessedFrames++;
                void encodeProcessedFrame(qf.frame);
            }

            segTotalInferenceTime += inferenceTime;
            segTotalBlurTime += performance.now() - blurStartTime;
            segTotalProcessingTime += performance.now() - qf.timestamp;
        }
    } finally {
        processingFrame = false;
    }
    */
}

// eslint-disable-next-line @typescript-eslint/require-await
async function initializeSegmentation(_config: SegmentationConfig): Promise<void> {
    // Segmentation is intentionally disabled in this release — onnxruntime-web
    // is heavy on low-end mobiles and segmentation is not yet wired into the UI.
    // `segInitialized` deliberately stays false so all gated call sites bypass
    // the queue and emit raw frames straight through. Re-enable the body below
    // when re-introducing background blur.
    infoLog?.log('Segmentation disabled in this release; ignoring config.segmentation');
    return;

    /*
    segConfig = config;
    ort.env.wasm.wasmPaths = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.23.2/dist/';
    ort.env.wasm.numThreads = 1;

    const modelUrl = SegmentationModelUrl;
    const sessionOptions: ort.InferenceSession.SessionOptions = {
        executionProviders: [{ name: 'webgpu', preferredLayout: 'NCHW' }],
        graphOptimizationLevel: 'all', executionMode: 'parallel',
        enableCpuMemArena: true, enableMemPattern: true,
        preferredOutputLocation: 'gpu-buffer', enableGraphCapture: true,
    };

    infoLog?.log('Loading segmentation model from:', modelUrl);
    onnxSession = await ort.InferenceSession.create(modelUrl, sessionOptions);
    infoLog?.log('Segmentation model loaded with WebGPU backend');

    const blurDevice = await ort.env.webgpu.device;
    await WebGPUManager.init(blurDevice);
    await initTensorWebGPU(blurDevice);
    await initBlurWebGPU(blurDevice);
    infoLog?.log('WebGPU resources initialized');

    const maskSize = config.inputWidth * config.inputHeight;
    const gpuDevice = WebGPUManager.get();
    const usage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST;
    outputGpuBuffer = gpuDevice.createBuffer({ size: maskSize * 4, usage });
    smoothedMaskBuffer = gpuDevice.createBuffer({ size: maskSize * 4, usage });

    outputTensor = ort.Tensor.fromGpuBuffer(outputGpuBuffer, {
        dataType: 'float32', dims: [1, 1, config.inputHeight, config.inputWidth],
        dispose: () => { // managed by worker
        },
    });

    resolvedModelConfig = config.modelConfig ?? getModelConfig(modelUrl);
    infoLog?.log(`Model config: format=${resolvedModelConfig.tensorFormat}`);

    initializeQueue(config);
    segInitialized = true;
    */
}

// ─── Encoding pipeline ──────────────────────────────────────────────────────

function processOneFrame(frame: VideoFrame): void {
    if (!processing) { frame.close(); return; }

    // Preview-only mode (no encoder): route through segmentation only
    if (!encoder) {
        if (blurEnabled && segInitialized) {
            enqueueFrame(frame);
        } else {
            // No blur, no encoder — just emit as preview (MSTG or RPC fallback)
            emitPreview(frame);
        }
        return;
    }

    if (!encoderConfig) { frame.close(); return; }

    slotArrivals++;
    if (encoder.getEncodeQueueSize() > ENCODER_BUSY_THRESHOLD) {
        // Encoder busy: replace pending slot. Newer frame wins.
        if (pendingEncoderFrame.hasValue)
            slotReplacements++;
        pendingEncoderFrame.push(frame);
        const now = performance.now();
        if (now - lastSlotCheckTime > slotWindowMs) {
            const replaceRate = slotReplacements / Math.max(1, slotArrivals);
            if (replaceRate > slotReplaceThreshold && !slotPressureNotified) {
                slotPressureNotified = true;
                warnLog?.log(`Sustained encoder slot pressure: replaceRate=${(replaceRate * 100).toFixed(1)}%`);
                void callbacks.onBackpressure(replaceRate, rpcNoWait);
            }
            slotReplacements = 0; slotArrivals = 0; lastSlotCheckTime = now;
        }
        return;
    }

    if (slotPressureNotified && slotArrivals > 30) {
        const now = performance.now();
        if (now - lastSlotCheckTime > slotWindowMs) {
            if (slotReplacements / Math.max(1, slotArrivals) < 0.05) slotPressureNotified = false;
            slotReplacements = 0; slotArrivals = 0; lastSlotCheckTime = now;
        }
    }

    // Encoder ready: if a stale pending frame is still in the slot, prefer
    // the latest arrival per the doc; close the stale one. Capture-then-clear
    // in a single sync block — the frame must not survive into encode() with
    // the slot still pointing at it (encoder closes input → slot would hold
    // a closed VideoFrame).
    pendingEncoderFrame.clear();

    if (blurEnabled && segInitialized) {
        enqueueFrame(frame);
    } else {
        void encodeProcessedFrame(frame);
    }
}

async function encodeProcessedFrame(frame: VideoFrame): Promise<void> {
    if (!encoder || !processing || !encoderConfig) { frame.close(); return; }
    // Drop frames during a codec switch — feeding a closed/transitioning
    // encoder produces a per-frame InvalidStateError that floods the console
    // and stalls the main thread. Re-enabled in switchCodec's finally.
    if (switchInProgress) { frame.close(); return; }
    // Cheap state guard: if the encoder isn't configured (still warming up,
    // or closed by a real WebCodecs error) skip the encode call. The
    // dead-encoder watchdog already escalates after framesWithoutOutput >= 30.
    if (encoder.getState() !== 'configured') { frame.close(); return; }

    // Track the live VideoFrame reference at each pipeline stage so the catch
    // path can close whatever's still owned. Without this, an exception between
    // sourceFrame creation and encoder.encode() leaks the intermediate frame
    // (browser console: `VideoFrame was garbage collected without being closed`).
    // The encoder wrapper closes its argument in finally, so once we hand off
    // to encode() this variable is set to null.
    let liveFrame: VideoFrame | null = frame;
    let sourceFrame: VideoFrame | null = null;
    try {
        if (startTimestamp === undefined) {
            startTimestamp = frame.timestamp;
            sourceStartedAtMs = serverClockNow(streamCtx);
            infoLog?.log(`Start timestamp set to ${startTimestamp}μs, sourceStartedAtMs=${sourceStartedAtMs.toFixed(0)}`);
        }

        // Source frames carry their original (non-rebased) timestamps through
        // the pipeline. Rebase to the recording anchor + int rounding happens
        // at chunk emission (deliverChunkToStream / onSerializedChunk) — this
        // avoids one VideoFrame allocation per source frame that would
        // otherwise be needed just to stamp a normalized timestamp.
        sourceFrame = frame;
        liveFrame = sourceFrame;

        let processedFrame: VideoFrame;
        if (downscaler) {
            // WebGPU path: keeps frame on GPU. Uses VideoFrame.rotation when set,
            // else senderRotationDeg (main-thread supplies from screen.orientation).
            // downscaler.process closes its input internally; sourceFrame is gone.
            const results = await downscaler.process(sourceFrame, {
                fallbackRotationDeg: senderRotationDeg,
            });
            sourceFrame = null;
            processedFrame = results[0].frame;
            liveFrame = processedFrame;
            // Simulcast extras — feed each additional downscale result to its layer
            // encoder. Encoders stamp SpatialLayerId on every emitted chunk via their
            // ctor-bound id, so the fan-out path tags frames automatically. The
            // extra encoders close their input in finally regardless of success.
            //
            // Track ownership of each results[i] frame (i ≥ 1; results[0] is
            // processedFrame, owned by liveFrame for the outer catch). `owned[i]
            // === false` means the frame has already been closed or handed to an
            // encoder that will close it. Any entry still `true` after the
            // simulcast block must be closed in the finally — otherwise an
            // exception escaping from the encode loop or the orphan cleanup
            // (e.g., logging throw, async-microtask crash) would leak the frame.
            const ownedExtras = new Array<boolean>(results.length).fill(true);
            ownedExtras[0] = false; // results[0] = processedFrame, tracked via liveFrame
            try {
                const expectedEncodeLayerCount = 1 + extraLayerEncoders.length;
                registerEncodeFrameCost(processedFrame.timestamp, expectedEncodeLayerCount);
                if (extraLayerEncoders.length > 0) {
                    for (let i = 0; i < extraLayerEncoders.length; i++) {
                        const idx = i + 1;
                        const extra = results[idx];
                        try {
                            extraLayerEncoders[i].encode(extra.frame, nextFrameIsKeyFrame);
                            ownedExtras[idx] = false; // encoder owns + closes
                        } catch (e) {
                            errorLog?.log(`Extra layer ${i + 1} encode error:`, e);
                            try { extra.frame.close(); } catch { /* already closed */ }
                            ownedExtras[idx] = false;
                        }
                    }
                }
                // Orphan cleanup: close any results beyond what extras consume
                // (transient length mismatch during a reconfig).
                for (let i = extraLayerEncoders.length + 1; i < results.length; i++) {
                    try { results[i].frame.close(); } catch { /* already closed */ }
                    ownedExtras[i] = false;
                }
            } finally {
                // Belt-and-braces: close anything still owned. Normal flow
                // leaves nothing here; this only fires if something above
                // threw before the per-iter handler could mark the entry.
                for (let i = 1; i < results.length; i++) {
                    if (!ownedExtras[i]) continue;
                    try { results[i].frame.close(); } catch { /* already closed */ }
                }
            }
        } else {
            // CPU resize path (older browsers without WebGPU). Source ts is
            // preserved through resizeFrame; chunk-level rebase handles
            // recording-anchor offset.
            const resized = resizeFrame(sourceFrame, encoderConfig.width, encoderConfig.height, resizeCanvas, resizeCtx, needsRotation);
            // resizeFrame closes its input when it produces a new frame; the
            // returned `frame` is the live one.
            sourceFrame = null;
            processedFrame = resized.frame;
            liveFrame = processedFrame;
            resizeCanvas = resized.canvas;
            resizeCtx = resized.ctx;
        }

        // WYSIWYG preview: clone the processed frame (post-rotate, post-downscale,
        // post-blur — exactly what we hand to the encoder) and emit to the preview
        // sink. Encoder still consumes the original `processedFrame` below; clone
        // is independent and closed by the MSTG writer / RPC consumer.
        if (previewWriter && !previewWriterClosed) {
            try {
                const previewClone = new VideoFrame(processedFrame, { timestamp: processedFrame.timestamp });
                emitPreview(previewClone);
            } catch (e) {
                if (!loggedPreviewCloneError) {
                    loggedPreviewCloneError = true;
                    warnLog?.log('Preview clone failed:', e);
                }
            }
        }

        // Optional YUV pre-conversion — disabled by default since HW encoders accept RGBA natively.
        // Enable via EncoderConfig.preConvertYuv for devices where pre-conversion helps encoding perf.
        // When the WebGPU downscaler is active its output is a GPU-resident
        // canvas-backed VideoFrame; running copyTo→new VideoFrame here forces a
        // GPU→CPU readback + re-upload, which is exactly the round-trip we
        // designed the downscaler path to avoid. Skip and let the HW encoder
        // consume RGBA directly.
        if (encoderConfig.preConvertYuv && downscaler) {
            if (!loggedPreConvertSkipped) {
                loggedPreConvertSkipped = true;
                warnLog?.log('preConvertYuv ignored: WebGPU downscaler active (output already GPU-resident, HW encoder consumes RGBA)');
            }
        }
        else if (encoderConfig.preConvertYuv) {
            const format = processedFrame.format as string | null;
            const isAlreadyYuv = format === 'NV12' || format === 'I420'
                || format === 'I420A' || format === 'I422' || format === 'I444' || format === 'NV12A';

            if (!isAlreadyYuv) {
                let converted = false;
                for (const fmt of ['I420', 'NV12', 'I420A', 'I422'] as const) {
                    try {
                        const size = processedFrame.allocationSize({ format: fmt });
                        const buf = new ArrayBuffer(size);
                        const layout = await processedFrame.copyTo(buf, { format: fmt });
                        const yuvFrame = new VideoFrame(buf, {
                            format: fmt, codedWidth: processedFrame.codedWidth, codedHeight: processedFrame.codedHeight,
                            timestamp: processedFrame.timestamp, duration: processedFrame.duration ?? undefined,
                            layout, colorSpace: processedFrame.colorSpace,
                        });
                        processedFrame.close();
                        processedFrame = yuvFrame;
                        liveFrame = processedFrame;
                        converted = true;
                        break;
                    } catch { continue; }
                }

                if (!converted) {
                    try {
                        const result = cpuRgbaToI420(processedFrame, resizeCanvas, resizeCtx);
                        // cpuRgbaToI420 closes its input and returns a fresh I420 frame.
                        processedFrame = result.frame;
                        liveFrame = processedFrame;
                        resizeCanvas = result.canvas;
                        resizeCtx = result.ctx;
                    } catch (e) {
                        if (!loggedI420Error) { loggedI420Error = true; warnLog?.log('All YUV conversion methods failed:', String(e)); }
                    }
                }
            }
        }

        // Frames carry source-timeline timestamps; primary + simulcast extras
        // share one timeline here. Rebase to recording-anchor offsets happens
        // post-encode in onEncoderOutput.

        // Let the encoder decide keyframes based on its keyframeInterval config
        // (set by recording-service.ts: ~2s screencast, ~3s webcam)
        const forceKf = nextFrameIsKeyFrame;
        nextFrameIsKeyFrame = false;
        // Cache a handle to the frame for the screencast heartbeat BEFORE
        // encoder.encode() — the encoder closes the frame it receives.
        // clone() shares underlying pixel data, so this is a ref bump not a copy.
        if (streamCtx.streamKind === 1) {
            if (lastEncodedFrame) lastEncodedFrame.close();
            lastEncodedFrame = processedFrame.clone();
            lastEncodedTimestampUs = processedFrame.timestamp;
            lastEncodedFrameAt = performance.now();
        }
        // Encoder.encode() closes processedFrame in its finally regardless of
        // success/throw — drop our live-tracking now so the catch below doesn't
        // try to close an already-closed frame.
        liveFrame = null;
        if (!downscaler)
            registerEncodeFrameCost(processedFrame.timestamp, 1);
        encoder.encode(processedFrame, forceKf);
        framesWithoutOutput++;

        // Detect dead encoder: error seen + 30 frames (~1s @ 30fps) with zero
        // output. Reduced from 90 (3s) — the retry-with-backoff in
        // WebCodecsEncoder.initialize() already gives the encoder ~1.5s to recover
        // from NVENC contention before reporting failure, so the watchdog only
        // needs to catch errors that slipped past retry (codec genuinely broken).
        if (encoderErrorSeen && framesWithoutOutput > 30 && !encoderFailed) {
            encoderFailed = true;
            const codec = encoderConfig.codec;
            errorLog?.log(`Encoder dead: ${codec} — ${framesWithoutOutput} frames with no output after error`);
            void callbacks.onEncoderFailed(codec, rpcNoWait);
        }
    } catch (error) {
        errorLog?.log('Error encoding frame:', error);
        // Whichever stage we got to, close the still-owned frame to avoid
        // GC-without-close warnings.
        if (liveFrame) {
            try { liveFrame.close(); } catch { /* already closed */ }
        }
    }
}

function onEncoderOutput(chunkData: EncodedChunkData): void {
    framesWithoutOutput = 0;
    encoderErrorSeen = false;
    recordEncodeLayerCost(
        chunkData.timestamp,
        chunkData.spatialLayerId ?? 0,
        chunkData.encodeTimeMs);
    // First chunk after start anchors the streaming-stall watchdog. The
    // watchdog won't fire while this is 0; the helper is also called below
    // when the pipeline reaches the 'streaming' status to refresh the clock
    // on healthy progress.
    if (streamingEnabled && streamProgressAt === 0)
        streamProgressAt = performance.now();

    // Drain pending encoder slot if base encoder freed up. Slot decisions
    // are gated on the base encoder's queue size only, so only base-layer
    // outputs trigger a drain. Capture-then-clear before re-entering the
    // pipeline so the frame is not double-owned.
    if (chunkData.spatialLayerId === 0
        && pendingEncoderFrame.hasValue
        && encoder
        && encoder.getEncodeQueueSize() <= ENCODER_BUSY_THRESHOLD) {
        const toEncode = pendingEncoderFrame.take()!;
        if (blurEnabled && segInitialized) enqueueFrame(toEncode);
        else void encodeProcessedFrame(toEncode);
    }

    const chunkBuffer = new ArrayBuffer(chunkData.byteLength);
    chunkData.chunk.copyTo(new Uint8Array(chunkBuffer));

    let actualCodec = encoderConfig!.codec;
    let descBuffer: ArrayBuffer | undefined;

    if (chunkData.type === 'key' && chunkData.metadata?.decoderConfig?.description) {
        const desc = chunkData.metadata.decoderConfig.description;
        let sourceArray: Uint8Array;
        if (desc instanceof ArrayBuffer) sourceArray = new Uint8Array(desc);
        else if (desc instanceof SharedArrayBuffer) sourceArray = new Uint8Array(desc);
        else if (ArrayBuffer.isView(desc)) sourceArray = new Uint8Array(desc.buffer, desc.byteOffset, desc.byteLength);
        else sourceArray = new Uint8Array(desc as ArrayBuffer);

        descBuffer = new ArrayBuffer(sourceArray.byteLength);
        new Uint8Array(descBuffer).set(sourceArray);

        if (isAvcCDescription(descBuffer) && !encoderConfig!.codec.startsWith('avc1')) {
            // Bump derived AVC level to admit the largest tier the ladder will encode.
            // Encoders pick the minimum level for THEIR input dims; the base layer
            // (e.g. 320×180) yields Level 3.0, but the 720p / 1080p simulcast extras
            // need Level 3.1 / 4.0. Without this bump the extra encoders fail with
            // `NotSupportedError ... AVC level (3.0) ... codec string (0x1E)`.
            const max = ladderMaxDims();
            const minLevelByte = pickAvcLevelByte(max.width, max.height);
            const derivedCodec = deriveAvcCodecFromDescription(descBuffer, minLevelByte);
            warnLog?.log(`Encoder output mismatch: configured=${encoderConfig!.codec} but output is avcC, correcting to ${derivedCodec} (ladder max ${max.width}x${max.height})`);
            actualCodec = derivedCodec;
            encoderConfig!.codec = derivedCodec;
        }
    }

    // Rebase chunk timestamp from source-camera timeline onto recording-anchor
    // (startTimestamp, set on first source frame). Math.round keeps ticks
    // int64-safe — sub-µs fractions on MSTP-wrapped getUserMedia would
    // otherwise propagate and force msgpack float64 (server rejects).
    //
    // Edge: after a videoStream-disposal reset (deliverChunkToStream sets
    // startTimestamp = undefined), in-flight chunks for frames encoded under
    // the prior anchor land here before the next source frame reinitializes
    // the anchor. Those chunks carry stale source-timeline timestamps with no
    // valid anchor to rebase against — drop silently. The new stream starts
    // fresh on the first chunk after the next source frame.
    if (startTimestamp === undefined) {
        debugLog?.log('Dropping in-flight chunk: recording anchor not set (reset in progress)');
        return;
    }
    const rebasedTs = Math.round(chunkData.chunk.timestamp - startTimestamp);

    if (streamingEnabled) {
        deliverChunkToStream(chunkBuffer, rebasedTs, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer,
            chunkData.temporalLayerId, chunkData.spatialLayerId,
            chunkData.width, chunkData.height);
    } else {
        void callbacks.onSerializedChunk(
            chunkBuffer, rebasedTs, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer, rpcNoWait);
    }
}

function deliverChunkToStream(
    chunkBytes: ArrayBuffer,
    timestamp: number,
    duration: number,
    isKeyFrame: boolean,
    codec: string,
    sequenceNumber: number,
    descriptionBytes?: ArrayBuffer,
    temporalLayerId?: number,
    spatialLayerId?: number,
    chunkWidth?: number,
    chunkHeight?: number
): void {
    // Detect sender disconnect BEFORE normalizing timestamps.
    //
    // With Fusion 12.3.25 + allowReconnect=true on the PushVideo RpcStream, same-peer
    // WS reconnects no longer dispose the sender — Fusion's $sys.Reconnect + real-time
    // resume (skip-to-next-keyframe via canSkipTo) handle continuity transparently.
    //
    // isDisposed therefore only fires on peer-change (sharedObjects.disconnectAll →
    // AbortSignal → source generator exits → stream.whenSent resolves → finally
    // { isDisposed = true }) or after the server-side MaxLiveDuration CTS. In both
    // cases the next PushVideo call targets a new server instance / new chat entry,
    // so the first frame must start a fresh timing anchor at offset 0 — otherwise
    // the viewer sees offsetMs≈past-stream-length and stalls.
    if (videoStream?.isDisposed) {
        // Causes: RPC peer-change (server restart / different hubId), or server
        // killed PushVideo via its frame-silence watchdog (WebcamFrameSilenceTimeout /
        // ScreencastFrameSilenceTimeout), or MaxLiveDuration. Recreate on next keyframe.
        // Reset startTimestamp so the next stream starts at offset 0 — avoids
        // handing the fresh server-side StreamStore a large non-zero baseline.
        warnLog?.log('VideoStream disposed — will recreate on next keyframe');
        videoStream = null;
        startTimestamp = undefined;
        sourceStartedAtMs = undefined;
        streamStatus = 'reconnecting: waiting for keyframe';
    }

    const chunkData = new Uint8Array(chunkBytes);

    // Use encoder-instance dims when provided (simulcast extras differ from primary),
    // fall back to primary's encoderConfig for single-encoder streams.
    const frameWidth = chunkWidth ?? encoderConfig!.width;
    const frameHeight = chunkHeight ?? encoderConfig!.height;

    // Active encoder ladder snapshot: base encoder is always present (id=0),
    // extras occupy ids 1..N where N = extraLayerEncoders.length. The receiver
    // uses [min, max] to know the full layer range without having to observe
    // every one. Constant across the burst on a given keyframe boundary.
    const minSpatialLayerId = 0;
    const maxSpatialLayerId = extraLayerEncoders.length;

    const frame: VideoStreamFrame = {
        offset: microsecondsToTicks(Math.round(timestamp)),
        duration: microsecondsToTicks(Math.round(duration)),
        isKeyFrame,
        width: frameWidth, height: frameHeight,
        data: chunkData, codec: isKeyFrame ? codec : undefined,
        temporalLayerId: temporalLayerId,
        spatialLayerId: spatialLayerId,
        minSpatialLayerId,
        maxSpatialLayerId,
        // Source dims piggybacked on keyframes only — server uses them to
        // recompute its max-quality ceiling when the window is resized mid-stream.
        sourceWidth: isKeyFrame ? sourceWidth : undefined,
        sourceHeight: isKeyFrame ? sourceHeight : undefined,
    };

    // Description handling:
    // - AVC AnnexB: SPS/PPS embedded inline in every keyframe → skip description entirely.
    // - Encoder emitted description on this keyframe → forward as-is, refresh per-layer cache.
    // - Encoder omitted description on this keyframe → fill from per-layer cache so the
    //   receiver's decoder can always reconfigure() (HEVC requires description on every
    //   configure; Chrome is allowed by spec to omit it on later keyframes).
    // Keyed by spatialLayerId because HVCC bytes differ per resolution in simulcast.
    const isAvcAnnexB = encoderConfig!.codec.startsWith('avc1');
    if (isKeyFrame && !isAvcAnnexB) {
        const layerId = spatialLayerId ?? 0;
        let descBytes: Uint8Array | null = null;
        if (descriptionBytes && descriptionBytes.byteLength > 0) {
            descBytes = new Uint8Array(descriptionBytes);
            storedDescriptionBytesByLayer.set(layerId, descBytes);
        } else {
            descBytes = storedDescriptionBytesByLayer.get(layerId) ?? null;
            if (!descBytes)
                warnLog?.log(`Keyframe for layer ${layerId} has no description and no cached entry`);
        }
        if (descBytes) {
            frame.description = descBytes;
            if (layerId === 0 && !codecSettings) {
                let binary = '';
                for (const byte of descBytes) binary += String.fromCharCode(byte);
                codecSettings = btoa(binary);
                debugLog?.log(`Captured codec description for layer ${layerId}: ${descBytes.length} bytes`);
            }
        }
    }

    if (!videoStream) {
        const isAV1 = encoderConfig!.codec.startsWith('av01');
        // AV1 + AVC AnnexB carry SPS/PPS inline, no description needed for stream creation.
        const canCreateStream = codecSettings ?? ((isAV1 || isAvcAnnexB) && isKeyFrame);
        if (canCreateStream) {
            const settings = codecSettings ?? '';
            infoLog?.log(`Creating VideoStream: codec=${encoderConfig!.codec}, ${encoderConfig!.width}x${encoderConfig!.height}, codecSettings=${settings.length} chars`);
            videoStream = new InternalVideoStream(
                {
                    codec: encoderConfig!.codec,
                    width: encoderConfig!.width,
                    height: encoderConfig!.height,
                    sourceWidth: sourceWidth || encoderConfig!.width,
                    sourceHeight: sourceHeight || encoderConfig!.height,
                    codecSettings: settings,
                },
                streamCtx,
                sourceStartedAtMs ?? serverClockNow(streamCtx),
                lastVideoStream?.whenDisposed,
            );
            lastVideoStream = videoStream;
            streamRecreations++;
            infoLog?.log(`deliverChunkToStream: startTimestamp=${((startTimestamp ?? 0) / 1000).toFixed(0)}ms, firstChunkOffsetMs=${(timestamp / 1000).toFixed(0)}`);
            for (const buffered of pendingStreamFrames) videoStream.addFrame(buffered);
            pendingStreamFrames = [];
            videoStream.addFrame(frame);
            streamStatus = 'streaming';
            streamProgressAt = performance.now();
            void callbacks.onStreamCreated(settings, rpcNoWait);
        } else {
            streamStatus = 'waiting for codec description';
            pendingStreamFrames.push(frame);
        }
    } else {
        videoStream.addFrame(frame);
    }
}

// Yield a macrotask so the browser can release a previous HW codec slot before
// a new one is allocated. Microtask alone is not enough — Chrome's WebCodecs
// implementation releases NVENC/VA-API slots on the next task tick. Used in
// places that close + (re)create encoders (codec switch, simulcast rebuild).
async function awaitHwReleased(): Promise<void> {
    await Promise.resolve();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
}

// Dedupe error logs by key within a 1 s window. Identical errors fired
// per-frame (e.g. a closed encoder hit by a stuck frame pump) used to flood
// the inspector pipe and freeze the main thread. The gate in
// encodeProcessedFrame already prevents the per-frame case; this is a second
// line of defence for real async error cascades.
const ERROR_LOG_DEDUPE_WINDOW_MS = 1000;
const errorLogLastSeenMs = new Map<string, number>();
function shouldLogError(key: string): boolean {
    const now = performance.now();
    const last = errorLogLastSeenMs.get(key) ?? 0;
    if (now - last < ERROR_LOG_DEDUPE_WINDOW_MS) return false;
    errorLogLastSeenMs.set(key, now);
    return true;
}

function onEncoderError(error: Error): void {
    if (shouldLogError(`enc:${error.name}:${error.message}`))
        errorLog?.log('Encoder error:', error.name, error.message);
    encoderErrorSeen = true;
    lastStreamError = `encoder: ${error.name} ${error.message}`;

    // Async error → primary encoder transitions to 'closed'. No further frames
    // will reach encode() (encodeProcessedFrame early-returns when state !=
    // 'configured'), so the frame-count watchdog can't fire. Extras-only errors
    // leave primary alive; this branch skips them and the regular watchdog
    // stays in charge for primary-stalled-without-error scenarios.
    if (encoderFailed || !encoder || !encoderConfig || encoder.getState() !== 'closed') return;

    const codec = encoderConfig.codec;
    const now = performance.now();

    // Same-codec rebuild path: try once per session before falling back.
    // `encoderRebuildAttempted` resets on switchCodec / start; cooldown covers
    // the multi-error storm that can come from one underlying failure (Chrome
    // sometimes fires `error` twice during MediaCodec teardown).
    if (!encoderRebuildAttempted
        && !encoderRebuildInFlight
        && (lastEncoderRebuildAtMs === 0 || now - lastEncoderRebuildAtMs > ENCODER_REBUILD_COOLDOWN_MS)) {
        encoderRebuildAttempted = true;
        encoderRebuildInFlight = true;
        lastEncoderRebuildAtMs = now;
        warnLog?.log(`Encoder ${codec} died (${error.name} ${error.message}); attempting same-codec rebuild`);
        void encoder.rebuild()
            .then(() => {
                encoderRebuildInFlight = false;
                infoLog?.log(`Encoder rebuild succeeded: ${codec}`);
                // New session means fresh AVCC/HVCC description bytes on the
                // next keyframe — drop the cached layer-0 entry so the worker
                // re-emits the description for downstream consumers.
                storedDescriptionBytesByLayer.delete(0);
            })
            .catch((rebuildErr: unknown) => {
                encoderRebuildInFlight = false;
                const e = rebuildErr instanceof Error ? rebuildErr : new Error(String(rebuildErr));
                warnLog?.log(`Encoder rebuild failed: ${e.name} ${e.message} — triggering fallback`);
                if (encoderFailed) return;
                encoderFailed = true;
                void callbacks.onEncoderFailed(codec, rpcNoWait);
            });
        return;
    }

    encoderFailed = true;
    warnLog?.log(`Encoder dead from async error: ${codec} (${error.name}) — triggering fallback`);
    void callbacks.onEncoderFailed(codec, rpcNoWait);
}

// Try to reuse a pooled encoder. Returns true if reused. Match by codec
// CATEGORY (av1/hevc/vp9/h264), not exact codec string — HEVC L3.1 vs L4.0
// (or AVC level changes) share the same NVENC session and `configure()` does
// a level reconfigure on the existing instance. Different category → close
// pool, create new (NVENC release will overlap with new acquire — best we can
// do without keeping multiple slots).
function tryAdoptPooledPrimary(targetCodec: string): boolean {
    if (poolExpireTimer !== null) {
        clearTimeout(poolExpireTimer);
        poolExpireTimer = null;
    }
    if (!pooledPrimary) return false;
    const poolCategory = getCodecCategory(pooledPrimary.codec);
    const targetCategory = getCodecCategory(targetCodec);
    if (poolCategory !== targetCategory) {
        // Pool entry is wrong codec family — close it. Frees slot (eventually).
        infoLog?.log(`Encoder pool: category mismatch (pool=${poolCategory}/${pooledPrimary.codec}, want=${targetCategory}/${targetCodec}), evicting`);
        try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
        pooledPrimary = null;
        return false;
    }
    // Same codec family — reuse. Caller updates encoder.setConfig() with the
    // new pipeline's config (possibly different level/dims/bitrate), then
    // initializeEncoders() calls configure() which is a reconfigure on the
    // existing NVENC session — no new HW slot acquired.
    infoLog?.log(`Encoder pool: reusing primary (${pooledPrimary.codec} ${pooledPrimary.width}x${pooledPrimary.height} → ${targetCodec})`);
    encoder = pooledPrimary.encoder;
    encoder.reset();
    // Restore the previously emitted description so we can build codecSettings
    // immediately on the first reused-session keyframe (Chrome won't re-emit it).
    if (pooledPrimary.description) {
        storedDescriptionBytesByLayer.set(0, pooledPrimary.description);
    }
    pooledPrimary = null;
    return true;
}

// Park the primary encoder for reuse on next start. Closes extras (per-session).
// TTL timer evicts the pool if no restart within POOL_TTL_MS.
function parkPrimaryEncoder(): void {
    if (encoder && encoderConfig) {
        // Evict any stale pool entry (shouldn't happen but be safe).
        if (pooledPrimary) {
            try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
        }
        pooledPrimary = {
            encoder,
            codec: encoderConfig.codec,
            width: encoderConfig.width,
            height: encoderConfig.height,
            description: storedDescriptionBytesByLayer.get(0) ?? null,
        };
        infoLog?.log(`Encoder pool: parked primary (${encoderConfig.codec} ${encoderConfig.width}x${encoderConfig.height}), TTL ${POOL_TTL_MS}ms`);
        encoder = null;
        if (poolExpireTimer !== null) clearTimeout(poolExpireTimer);
        poolExpireTimer = setTimeout(() => {
            poolExpireTimer = null;
            if (pooledPrimary) {
                infoLog?.log('Encoder pool: TTL expired, closing primary');
                try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
                pooledPrimary = null;
            }
        }, POOL_TTL_MS);
    }
    // Always close extras — simulcast configs are per-session.
    for (const e of extraLayerEncoders) {
        try { e.close(); } catch { /* already closed */ }
    }
    extraLayerEncoders = [];
}

// Creates the base encoder (SpatialLayerId=0) plus any simulcast extras declared
// in `config.spatialLayers`. Base encoder always present; extras empty in P2P mode.
function setupEncoders(config: VideoProcessingConfig): void {
    encoderConfig = config.encoder;
    // Fresh encoder instances emit fresh HVCC/AVCC on their first keyframe — drop
    // any cached descriptions from prior encoder generation to avoid stale bytes.
    storedDescriptionBytesByLayer.clear();
    // Reuse pooled encoder if codec matches — keeps NVENC session held across
    // stop/start, avoiding `OperationError: Encoder initialization error` from
    // the previous session's slow async release.
    if (tryAdoptPooledPrimary(config.encoder.codec)) {
        // Update the pooled encoder's config to the new pipeline's params.
        // initializeEncoders() will call configure() with these dims/bitrate.
        encoder!.setConfig(config.encoder);
    } else {
        encoder = new WebCodecsEncoder(config.encoder, onEncoderOutput, onEncoderError, 0);
    }

    // Drop any prior extras (covers switchCodec re-entry and re-init scenarios).
    for (const e of extraLayerEncoders) {
        try { e.close(); } catch { /* already closed */ }
    }
    extraLayerEncoders = [];

    const layers = config.spatialLayers ?? [];
    for (let i = 0; i < layers.length; i++) {
        const layer = alignLayerToEncoderOrientation(layers[i]);
        const layerCfg: EncoderConfig = {
            ...config.encoder,
            width: layer.width,
            height: layer.height,
            bitrate: layer.bitrate,
            scalabilityMode: layer.scalabilityMode ?? config.encoder.scalabilityMode,
        };
        const spatialId = i + 1; // base layer consumed index 0
        const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
        extraLayerEncoders.push(extra);
        infoLog?.log(`Simulcast layer ${spatialId}: ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps`);
    }
    // .initialize() (encoder.configure) deferred to first frame so the first
    // configure call uses post-reconcile dims — see initializeEncoders().
    encodersInitialized = false;
}

// Calls .initialize() (encoder.configure) on the base + extra encoders.
// Idempotent. Stream-mode callers invoke this after first-frame dim reconcile
// so the very first configure call lands on final dims; RPC fallback mode
// invokes it eagerly during start-up since it has no first-frame reconcile.
function initializeEncoders(): void {
    if (encodersInitialized) return;
    encodersInitialized = true;
    if (!encoder) return;
    try { encoder.initialize(); } catch { /* error already surfaced via onEncoderError */ }
    for (const e of extraLayerEncoders) {
        try { e.initialize(); } catch { /* error already surfaced via onEncoderError */ }
    }
}

// Largest (width, height) across base + extras. Used to pick an AVC level
// that admits every layer in the ladder — see deriveAvcCodecFromDescription
// site for why a base-derived level is too low for simulcast.
function ladderMaxDims(): { width: number; height: number } {
    let w = encoderConfig?.width ?? 0;
    let h = encoderConfig?.height ?? 0;
    for (const e of extraLayerEncoders) {
        const s = e.getStats();
        if (s.configuredWidth > w) w = s.configuredWidth;
        if (s.configuredHeight > h) h = s.configuredHeight;
    }
    return { width: w, height: h };
}

// Map a layer's (w, h) onto the base encoder's current orientation. The ladder
// is built from camera-sensor dims (always landscape on iOS — see
// media-capture.ts comment) so a portrait-oriented sender receives landscape
// extras unless we transpose. Mirrors the reconfigure RPC handler's swap
// (search "Preserve encoder orientation"). Bitrate stays put — it's a function
// of pixel count, which is rotation-invariant. Returns the same shape so
// callers can use the result interchangeably with the input layer.
function alignLayerToEncoderOrientation(layer: SpatialLayerConfig): SpatialLayerConfig {
    if (!encoderConfig) return layer;
    const inSmall = Math.min(layer.width, layer.height);
    const inLarge = Math.max(layer.width, layer.height);
    const isPortrait = encoderConfig.height > encoderConfig.width;
    const w = (isPortrait ? inSmall : inLarge) & ~1;
    const h = (isPortrait ? inLarge : inSmall) & ~1;
    if (w === layer.width && h === layer.height) return layer;
    return { ...layer, width: w, height: h };
}

// Cheap structural match: same length AND identical (w, h, bitrate) per index.
// Used by setSpatialLayers to skip a no-op rebuild — repeated server pushes of
// the same ladder are common and we don't want to drain the encoder pipeline
// on every duplicate.
function extraLayerCountMatches(layers: SpatialLayerConfig[]): boolean {
    if (layers.length !== extraLayerEncoders.length) return false;
    for (let i = 0; i < layers.length; i++) {
        const live = extraLayerEncoders[i].getStats();
        const want = alignLayerToEncoderOrientation(layers[i]);
        if (live.configuredWidth !== want.width
            || live.configuredHeight !== want.height
            || live.configuredBitrate !== want.bitrate) return false;
    }
    return true;
}

function collectDownscaleTargets(config: VideoProcessingConfig): DownscaleTarget[] {
    return [
        { width: config.encoder.width, height: config.encoder.height },
        ...(config.spatialLayers ?? []).map(l => ({ width: l.width, height: l.height })),
    ];
}

// Builds the current downscaler target list from live encoder state. Primary
// dims come from encoderConfig (which is mutated by reconfigure + orientation
// reconcile); extras come from each live WebCodecsEncoder's configured dims.
// Used by any code path that rebuilds downscaler config mid-stream so that
// simulcast extras aren't accidentally dropped.
function currentDownscaleTargets(): DownscaleTarget[] {
    const targets: DownscaleTarget[] = [];
    if (encoderConfig) targets.push({ width: encoderConfig.width, height: encoderConfig.height });
    for (const e of extraLayerEncoders) {
        const s = e.getStats();
        targets.push({ width: s.configuredWidth, height: s.configuredHeight });
    }
    return targets;
}

// Init WebGPU downscaler for the full simulcast target list (base + any extras).
// Returns null if WebGPU is unavailable — caller then relies on the legacy canvas
// resizeFrame path (which only covers the primary layer; simulcast requires WebGPU).
async function initDownscaler(config: VideoProcessingConfig): Promise<void> {
    const targets = collectDownscaleTargets(config);
    if (downscaler) {
        downscaler.configure(targets);
        return;
    }
    try {
        const device = await WebGPUManager.init();
        downscaler = new WebGpuDownscaler(device);
        downscaler.configure(targets);
        infoLog?.log(`Downscaler initialized with ${targets.length} target(s)`);
    } catch (e) {
        warnLog?.log('WebGPU downscaler unavailable, falling back to canvas resize:', e);
        downscaler = null;
    }
}

// ─── Preview-only (MSTG) helpers ────────────────────────────────────────────

interface PreviewMSTG { writable: WritableStream<VideoFrame>; track: MediaStreamTrack }

// Initialise the preview MSTG and hand the output track to main. Idempotent —
// no-op if MSTG already set up. Returns true on success. Encoder modes call
// this opportunistically (no-op on browsers without MSTG, RPC fallback applies);
// previewOnly mode calls it as a hard requirement.
function ensurePreviewMSTG(): boolean {
    if (previewWriter && !previewWriterClosed) return true;
    const mstg = tryCreatePreviewMSTG();
    if (!mstg) return false;
    previewWriter = mstg.writable.getWriter();
    previewWriterClosed = false;
    void callbacks.onPreviewTrack(mstg.track, rpcNoWait);
    return true;
}

function tryCreatePreviewMSTG(): PreviewMSTG | null {
    const w = self as unknown as {
        VideoTrackGenerator?: VideoTrackGeneratorCtor;
        MediaStreamTrackGenerator?: MediaStreamTrackGeneratorCtor;
    };
    try {
        if (typeof w.VideoTrackGenerator === 'function') {
            const gen = new w.VideoTrackGenerator();
            return { writable: gen.writable, track: gen.track };
        }
        if (typeof w.MediaStreamTrackGenerator === 'function') {
            const gen = new w.MediaStreamTrackGenerator({ kind: 'video' });
            return { writable: gen.writable, track: gen.track };
        }
    } catch (e) {
        warnLog?.log('Failed to construct preview MSTG:', e);
    }
    return null;
}

async function previewReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value } = await inputReader.read();
            if (done) { infoLog?.log('Preview input ended'); break; }
            processOneFrame(value);
        }
    } catch (e) {
        errorLog?.log('previewReadLoop error:', e);
    } finally {
        try { inputReader.releaseLock(); } catch { /* ignore */ }
    }
}

// ─── Stream read loop ───────────────────────────────────────────────────────

async function streamReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value: rawFrame } = await inputReader.read();
            if (done) { infoLog?.log('Stream input ended'); break; }

            if (!encoder || !processing || !encoderConfig) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                rawFrame.close(); continue;
            }

            const frameRotation = rawFrame.rotation ?? null;

            if (!dimensionsReconciled) {
                dimensionsReconciled = true;
                const frameW = rawFrame.displayWidth;
                const frameH = rawFrame.displayHeight;
                const codedW = rawFrame.codedWidth;
                const codedH = rawFrame.codedHeight;
                sourceWidth = frameW;
                sourceHeight = frameH;
                infoLog?.log(`streamReadLoop: dimensions, display=${frameW}x${frameH}, coded=${codedW}x${codedH}, config=${encoderConfig.width}x${encoderConfig.height}, rotation=${frameRotation ?? 'N/A'}`);
                // Detect rotation: display dims are transposed vs encoder config
                // (MSTP gives raw sensor dims as displayWidth/Height)
                const isRotated = frameW === encoderConfig.height && frameH === encoderConfig.width
                    && frameW !== encoderConfig.width;
                // Detect rotation: Safari sets display dims to post-rotation (portrait) but pixel
                // buffer stays in sensor orientation (landscape) — coded dims reveal true pixel layout
                const isRotatedByCoded = !isRotated
                    && codedW === frameH && codedH === frameW && codedW !== frameW;
                let detection: OrientationStats['rotationDetection'] = 'none';
                if (isRotated || isRotatedByCoded) {
                    needsRotation = true;
                    detection = isRotated ? 'dimensions' : 'coded';
                    // Keep encoder config at portrait dimensions — resizeFrame() rotate90 will
                    // rotate landscape frames into portrait before encoding
                } else if (frameW !== encoderConfig.width || frameH !== encoderConfig.height) {
                    const inputPixels = frameW * frameH;
                    const configPixels = encoderConfig.width * encoderConfig.height;
                    if (inputPixels <= configPixels) {
                        // Input is smaller than config — reconfigure encoder to match
                        // (avoids upscaling which wastes CPU for no quality gain).
                        // Ensure even dims — video codecs require it; odd window/tab
                        // capture sizes would cause an encoder error.
                        const evenW = frameW & ~1;
                        const evenH = frameH & ~1;
                        warnLog?.log(`Display dimensions ${frameW}x${frameH} smaller than config ${encoderConfig.width}x${encoderConfig.height}, reconfiguring to ${evenW}x${evenH}`);
                        encoderConfig.width = evenW; encoderConfig.height = evenH;
                        // Downscaler first (sync), encoder second (async) — same invariant as
                        // orientation/RPC reconfigure paths. Without this the downscaler keeps
                        // its old slot target and feeds frames at the old dims into a smaller
                        // encoder, hitting the dim-mismatch guard and dropping every frame.
                        if (downscaler) downscaler.configure(currentDownscaleTargets());
                        if (encodersInitialized) {
                            await encoder.reconfigure({ width: evenW, height: evenH, bitrate: encoderConfig.bitrate });
                        }
                        if (segConfig) { segConfig.outputWidth = evenW; segConfig.outputHeight = evenH; }
                        void callbacks.onDimensionReconciled(evenW, evenH, rpcNoWait);
                    } else {
                        // Input is larger than config (e.g., 4K screen capture with 1080p encoder config).
                        // Keep encoder at configured resolution — resizeFrame() will downscale.
                        debugLog?.log(`Display dimensions ${frameW}x${frameH} larger than config ${encoderConfig.width}x${encoderConfig.height}, resize canvas will downscale`);
                    }
                }
                // Record rotation metadata even when no other detector fires — useful for diagnostics
                if (detection === 'none' && frameRotation !== null && frameRotation % 180 !== 0)
                    detection = 'metadata';
                orientationStats = {
                    firstDisplayWidth: frameW, firstDisplayHeight: frameH,
                    firstCodedWidth: codedW, firstCodedHeight: codedH,
                    firstRotation: frameRotation, lastRotation: frameRotation,
                    configuredWidth: encoderConfig.width, configuredHeight: encoderConfig.height,
                    needsRotation, rotationDetection: detection, framesSeen: 1,
                };
            } else if (orientationStats) {
                orientationStats.framesSeen++;
                orientationStats.lastRotation = frameRotation;
                // Track running max source dimensions so window resize / camera swap
                // that grows the source is visible to the server via subsequent
                // keyframes (see enqueueStreamingFrame — source dims attached on KF).
                const currentW = rawFrame.displayWidth;
                const currentH = rawFrame.displayHeight;
                if (currentW > sourceWidth) sourceWidth = currentW;
                if (currentH > sourceHeight) sourceHeight = currentH;
            }

            // Determine user-facing orientation from POST-rotation source dims,
            // not from the rotation value alone. Per-platform rationale:
            //   - iOS Safari MSTP: source landscape (1920×1080), `frame.rotation`
            //     null → fallback `senderRotationDeg=90` → swap → portrait. ✓
            //   - Android Chrome MSTP: source already-portrait (720×1280) when
            //     phone is portrait, `frame.rotation=0` → no swap → portrait. ✓
            //     (The old `displayPortrait = rotDeg === 90 || === 270` check
            //     misread Android's `rotation=0` as "wants landscape" and flipped
            //     the encoder mid-stream.)
            //   - Desktop: source landscape (1280×720), rotation=0 → no swap →
            //     landscape. Reconcile flips encoder away from the iOS-tuned
            //     startup-portrait transpose, as before.
            const rotDeg = frameRotation ?? senderRotationDeg;
            let finalW = rawFrame.displayWidth;
            let finalH = rawFrame.displayHeight;
            if (rotDeg === 90 || rotDeg === 270) {
                const t = finalW; finalW = finalH; finalH = t;
            }
            const displayPortrait = finalH > finalW;
            const encoderPortrait = encoderConfig.height > encoderConfig.width;
            if (displayPortrait !== encoderPortrait) {
                const newW = encoderConfig.height;
                const newH = encoderConfig.width;
                try {
                    encoderConfig.width = newW;
                    encoderConfig.height = newH;
                    // Downscaler first (sync) — see comment in `reconfigure` RPC
                    // above about the race window during encoder.reconfigure's
                    // `await` and why downscaler-first + dims-mismatch guard
                    // beats the Chrome HW-encoder top-left-crop fallback.
                    if (downscaler) downscaler.configure(currentDownscaleTargets());
                    // First-frame: encoder.configure() is deferred (encodersInitialized=false)
                    // so we just mutate encoderConfig — initializeEncoders() below will pick
                    // up the final dims. Mid-stream rotation flip (encoder already warm) hits
                    // the encoder.reconfigure() branch — by then HW encoder accepts it.
                    if (encodersInitialized) {
                        await encoder.reconfigure({ width: newW, height: newH, bitrate: encoderConfig.bitrate });
                    }
                    if (segConfig) { segConfig.outputWidth = newW; segConfig.outputHeight = newH; }
                    void callbacks.onDimensionReconciled(newW, newH, rpcNoWait);
                    infoLog?.log(`Orientation change: rotation=${rotDeg} → encoder ${newW}x${newH} (downscaler targets=${extraLayerEncoders.length + 1})`);
                } catch (e) {
                    warnLog?.log('Orientation reconfigure failed:', e);
                }
            }

            // Lazy-initialize encoders on first frame, after dim reconcile mutated
            // encoderConfig.width/height to its final values. This is the SINGLE
            // configure() call — no flip-flop, no HW encoder init crash on HEVC.
            if (!encodersInitialized) initializeEncoders();

            const frame = rawFrame;

            if (!vadSpeaking && vadRemoteStreamCount >= 2) {
                const now = performance.now();
                if (now - vadLastPassedFrameTime < vadReducedFrameIntervalMs) { frame.close(); continue; }
                vadLastPassedFrameTime = now;
            }

            processOneFrame(frame);
        }
    } catch (error) {
        if (processing) errorLog?.log('Stream read error:', error);
    } finally {
        try { inputReader.releaseLock(); } catch { /* ignore */ }
    }
}

function startScreencastHeartbeat(): void {
    if (heartbeatTimer) return;
    if (streamCtx.streamKind !== 1) return; // screencast only
    heartbeatTimer = setInterval(() => {
        if (!processing || !encoder || !encoderConfig) return;
        if (!videoStream || videoStream.isDisposed) return;
        if (!lastEncodedFrame) return;
        const silence = performance.now() - lastEncodedFrameAt;
        if (silence < SCREENCAST_HEARTBEAT_MS) return;

        try {
            // Monotonic timestamps: advance by the wall-clock gap since the last
            // frame we fed to the encoder (real or heartbeat). μs.
            const freshTs = lastEncodedTimestampUs + Math.max(1, Math.round(silence * 1000));
            // Constructing a new VideoFrame from lastEncodedFrame shares the
            // underlying pixel buffer (ref bump). The encoder closes `fresh`
            // after encode() — but lastEncodedFrame stays alive and reusable
            // for the next tick. Do NOT close+re-clone lastEncodedFrame here;
            // that would fail on the next tick because encoder.encode() has
            // already closed `fresh` before we'd attempt fresh.clone().
            const fresh = new VideoFrame(lastEncodedFrame, {
                timestamp: freshTs,
                duration: lastEncodedFrame.duration ?? undefined,
            });
            // Don't force keyframe here — encoder auto-promotes every
            // maxKeyFrameIntervalMs. P-frame on identical content is near-zero
            // bytes; keyframes land on schedule for mid-stream joiners.
            encoder.encode(fresh, false);
            lastEncodedTimestampUs = freshTs;
            lastEncodedFrameAt = performance.now();
            debugLog?.log(`Screencast heartbeat: re-encoded last frame after ${Math.round(silence)}ms of silence`);
        } catch (e) {
            warnLog?.log('Screencast heartbeat encode failed:', e);
        }
    }, SCREENCAST_HEARTBEAT_CHECK_MS);
}

function stopScreencastHeartbeat(): void {
    if (heartbeatTimer) { clearInterval(heartbeatTimer); heartbeatTimer = null; }
    if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
    lastEncodedFrameAt = 0;
    lastEncodedTimestampUs = 0;
}

// ─── Server implementation ──────────────────────────────────────────────────

export const serverImpl: VideoProcessingWorker = {
    ...sharedSettingsWorker,

    init: async (appConstants, sharedSettings: SharedSettingsSnapshot): Promise<void> => {
        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
    },

    startWithStream: async (config, frameInputStream): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (stream mode)...');
            applyStreamingConfig(config);
            senderRotationDeg = config.senderRotationDeg ?? 0;

            // Set up MSTG output BEFORE encoder so the preview track is delivered
            // to main while we're still in the awaited startup window — main can
            // read it synchronously after `startWithStream` resolves. No-op
            // (returns false) on browsers without MSTG; preview falls back to
            // the existing RPC `onPreviewFrame` callback path.
            ensurePreviewMSTG();

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            setupEncoders(config);
            await initDownscaler(config);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            startRecorderHealthAggregator();
            dimensionsReconciled = false;
            needsRotation = false;
            orientationStats = null;

            const inputReader = frameInputStream.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);
            startScreencastHeartbeat();

            infoLog?.log('Video processing worker started (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to start stream mode:', error);
            throw error;
        }
    },

    startPreviewWithTrack: async (config, track): Promise<void> => {
        try {
            infoLog?.log('Starting preview-only worker (MSTG mode)...');

            if (typeof MediaStreamTrackProcessor === 'undefined')
                throw new Error('MediaStreamTrackProcessor not available in worker scope');

            // Hand the output track to main BEFORE we start producing frames so
            // the consumer is wired up by the time the first frame arrives.
            if (!ensurePreviewMSTG())
                throw new Error('MediaStreamTrackGenerator/VideoTrackGenerator not available in worker scope');

            senderRotationDeg = config.senderRotationDeg ?? 0;
            if (config.segmentation) {
                await initializeSegmentation({
                    ...config.segmentation,
                    outputWidth: config.encoder.width,
                    outputHeight: config.encoder.height,
                });
                blurEnabled = true;
            }

            processing = true;
            inputTrack = track;

            const processor = new MediaStreamTrackProcessor({ track });
            const inputReader = processor.readable.getReader();
            streamReadLoopPromise = previewReadLoop(inputReader);

            infoLog?.log('Preview-only worker started (MSTG mode)');
        } catch (error) {
            errorLog?.log('Failed to start preview-only worker (MSTG mode):', error);
            // Roll back partial state so the caller can fall back cleanly.
            if (previewWriter) {
                try { await previewWriter.close(); } catch { /* ignore */ }
                previewWriter = null;
            }
            previewWriterClosed = false;
            throw error;
        }
    },

    startWithTrack: async (config, track): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (track transfer mode)...');

            // Check if MSTP is available in worker scope
            if (typeof MediaStreamTrackProcessor === 'undefined') {
                throw new Error('MediaStreamTrackProcessor not available in worker scope');
            }

            applyStreamingConfig(config);
            senderRotationDeg = config.senderRotationDeg ?? 0;

            // Set up MSTG output BEFORE encoder so the preview track is delivered
            // to main while we're still in the awaited startup window.
            ensurePreviewMSTG();

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            setupEncoders(config);
            await initDownscaler(config);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            startRecorderHealthAggregator();
            dimensionsReconciled = false;
            needsRotation = false;
            orientationStats = null;
            inputTrack = track;

            const processor = new MediaStreamTrackProcessor({ track });
            const inputReader = processor.readable.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);
            startScreencastHeartbeat();

            infoLog?.log('Video processing worker started (track transfer mode)');
        } catch (error) {
            errorLog?.log('Failed to start track transfer mode:', error);
            throw error;
        }
    },

    initialize: async (config): Promise<void> => {
        try {
            const isPreviewOnly = config.previewOnly === true;
            infoLog?.log(`Starting video processing worker (${isPreviewOnly ? 'preview-only' : 'RPC'} mode)...`);

            if (!isPreviewOnly) {
                applyStreamingConfig(config);
                // Encoder mode: opportunistic MSTG output. No-op on browsers
                // without MSTG; preview falls back to RPC `onPreviewFrame`.
                ensurePreviewMSTG();
            }
            senderRotationDeg = config.senderRotationDeg ?? 0;

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
            }

            if (!isPreviewOnly) {
                setupEncoders(config);
                await initDownscaler(config);
                // RPC fallback: no streamReadLoop first-frame reconcile fires here,
                // so the encoder must be configured eagerly with the supplied dims.
                initializeEncoders();
            }

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            startRecorderHealthAggregator();

            infoLog?.log(`Video processing worker started (${isPreviewOnly ? 'preview-only' : 'RPC'} mode)`);
        } catch (error) {
            errorLog?.log('Failed to initialize:', error);
            throw error;
        }
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    encodeFrame: async (frame): Promise<void> => { processOneFrame(frame); },

    // eslint-disable-next-line @typescript-eslint/require-await
    setVadState: async (speaking, remoteStreamCount): Promise<void> => {
        vadSpeaking = speaking;
        vadRemoteStreamCount = remoteStreamCount;
        if (speaking) vadLastPassedFrameTime = 0;
        debugLog?.log(`VAD state: speaking=${speaking}, remoteStreamCount=${remoteStreamCount}`);
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    setSenderRotation: async (rotationDeg): Promise<void> => {
        senderRotationDeg = ((rotationDeg % 360) + 360) % 360;
        infoLog?.log(`Sender rotation set to ${senderRotationDeg}°`);
    },

    reconfigure: async (params): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) { warnLog?.log('Cannot reconfigure: not active'); return; }
        // Preserve encoder orientation: map incoming dimensions by magnitude
        const inSmall = Math.min(params.width, params.height);
        const inLarge = Math.max(params.width, params.height);
        const isPortrait = encoderConfig.height > encoderConfig.width;
        params.width = (isPortrait ? inSmall : inLarge) & ~1;
        params.height = (isPortrait ? inLarge : inSmall) & ~1;
        infoLog?.log(`Reconfigure: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);
        encoderConfig.bitrate = params.bitrate; encoderConfig.width = params.width; encoderConfig.height = params.height;
        resizeCanvas = null; resizeCtx = null;
        // Order matters. `encoder.reconfigure` has an `await` boundary, so the
        // stream-read loop can interleave a frame between the downscaler and
        // encoder config updates. We reconfigure the downscaler FIRST (sync)
        // so subsequent frames immediately land at the new target dims. Any
        // frame that still sneaks through while the encoder is mid-reconfigure
        // hits the dims-mismatch guard in `WebCodecsEncoder.encode` and is
        // dropped — preferable to letting Chrome's HW encoder silently
        // top-left-crop an old-dim frame against the new config.
        if (downscaler) {
            // Preserve simulcast extras when reconfiguring the primary layer.
            // Extras stay at their initial dims — per-layer reconfigure is a
            // later-stage feature driven by VideoQualityPreset.MaxSpatialLayer.
            downscaler.configure(currentDownscaleTargets());
        }
        await encoder.reconfigure(params);
        if (segConfig && blurEnabled) { segConfig.outputWidth = params.width; segConfig.outputHeight = params.height; }
        // Drop cached heartbeat frame — its dimensions no longer match the encoder.
        if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
        resetRecorderHealthMetrics();
    },

    switchCodec: async (config: EncoderConfig, spatialLayers?: SpatialLayerConfig[]): Promise<void> => {
        if (!encoder) { warnLog?.log('Cannot switch codec: not active'); return; }
        infoLog?.log(`Switching codec to ${config.codec}`);
        streamStatus = 'switching codec';

        // Suppress frame output during codec switch — encoder.switchCodec() flushes
        // old-codec frames that must NOT leak into the new stream
        const wasStreaming = streamingEnabled;
        streamingEnabled = false;
        // Gate the frame pump for the duration of the switch. encodeProcessedFrame
        // checks this and drops incoming frames so encode() is never called against
        // the closed/transitioning encoder (the per-frame InvalidStateError flood
        // was the root of the multi-minute UI freeze observed in production).
        switchInProgress = true;
        let switchFailed = false;
        try {
            if (videoStream) {
                videoStream.complete();
                try { await videoStream.whenDisposed; } catch { /* ignore */ }
                videoStream = null;
            }
            codecSettings = null; startTimestamp = undefined; sourceStartedAtMs = undefined; pendingStreamFrames = []; storedDescriptionBytesByLayer.clear();
            if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
            pendingEncoderFrame.clear();
            // Synchronous configure() failure inside switchCodec already surfaces via
            // onEncoderError; the watchdog plus codec-exclusion list takes care of
            // the next fallback. Don't let a sync throw break the worker RPC.
            try { await encoder.switchCodec(config); } catch { switchFailed = true; /* error already surfaced via onEncoderError */ }
            // Drop old-codec simulcast extras; we rebuild them below with the new codec
            // so the ladder survives the switch. Without this rebuild every codec
            // switch permanently collapses the stream to base layer (regression
            // observed on AV1→H.264 audience changes: receivers stuck at 640×360).
            if (extraLayerEncoders.length > 0) {
                const extras = extraLayerEncoders.slice();
                extraLayerEncoders = [];
                infoLog?.log(`switchCodec: tearing down ${extras.length} simulcast extras`);
                for (const e of extras) {
                    try { await e.flush(); } catch { /* already closed elsewhere */ }
                    try { e.close(); } catch { /* already closed */ }
                }
            }

            encoderConfig = config; resizeCanvas = null; resizeCtx = null;
            // Rebuild simulcast extras on the new codec and restore downscaler targets
            // to match. If caller omits spatialLayers (P2P mode) we fall through to
            // a single base target and stay single-layer. Same orientation alignment
            // as setupEncoders / setSpatialLayers — see alignLayerToEncoderOrientation.
            const layers = (spatialLayers ?? []).map(alignLayerToEncoderOrientation);
            for (let i = 0; i < layers.length; i++) {
                const layer = layers[i];
                const layerCfg: EncoderConfig = {
                    ...config,
                    width: layer.width,
                    height: layer.height,
                    bitrate: layer.bitrate,
                    scalabilityMode: layer.scalabilityMode ?? config.scalabilityMode,
                };
                const spatialId = i + 1;
                // Yield a macrotask between successive HW encoder allocations.
                // Creating N encoders in the same microtask exceeds platform
                // HW slot release windows (NVENC/VA-API), and the second create
                // returns OperationError 'Encoder creation error' even though
                // the codec works fine in isolation. The yield is cheap and
                // happens only on codec switch.
                await awaitHwReleased();
                const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
                try { extra.initialize(); } catch { /* error already surfaced via onEncoderError */ }
                extraLayerEncoders.push(extra);
                infoLog?.log(`Simulcast layer ${spatialId} (post-switch): ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps`);
            }
            if (downscaler) downscaler.configure(currentDownscaleTargets());
        } finally {
            // Re-enable streaming and clear any frames that leaked during flush
            streamingEnabled = wasStreaming;
            switchInProgress = false;
        }
        // Diagnostic: explain what the encoder needs to resume streaming.
        // lastEncodedFrame is always null here (cleared above), so screencast
        // heartbeat can't fire until getDisplayMedia produces a new frame.
        // On a static screen this means the stream is stalled until user activity.
        if (streamCtx.streamKind === 1)
            streamStatus = 'stalled: waiting for screen activity after codec switch (heartbeat source lost)';
        else
            streamStatus = 'waiting for encoder output';
        pendingStreamFrames = [];
        startTimestamp = undefined;
        sourceStartedAtMs = undefined;
        slotReplacements = 0; slotArrivals = 0; lastSlotCheckTime = 0; slotPressureNotified = false;
        // Always reset encoderFailed and framesWithoutOutput on codec switch — the
        // new codec attempt deserves its own watchdog cycle. If switchCodec failed
        // synchronously, keep encoderErrorSeen=true so the watchdog fires again
        // for the new codec; otherwise clear it for a normal warmup window.
        encoderFailed = false; framesWithoutOutput = 0;
        // Reset streaming stall reference too — the new codec hasn't produced
        // any chunks yet, and re-using the pre-switch timestamp would let the
        // stall watchdog fire on the warmup window of a perfectly fine codec.
        streamProgressAt = 0;
        // New codec gets its own same-codec rebuild budget.
        encoderRebuildAttempted = false; encoderRebuildInFlight = false; lastEncoderRebuildAtMs = 0;
        if (!switchFailed) {
            encoderErrorSeen = false; lastStreamError = '';
        }
        resetRecorderHealthMetrics();
        infoLog?.log(`Codec switched ${switchFailed ? 'with error' : 'successfully'} (${extraLayerEncoders.length} simulcast extras rearmed)`);
    },

    // Hot simulcast reconfig: swap the extra-layer encoders without touching the
    // base encoder or the active VideoStream/RPC connection. Used to enable
    // simulcast mid-recording when a second peer joins (or to drop it when the
    // call collapses to P2P), without the stop/start cascade documented in
    // commit 2de3f2617. No-op when the requested ladder matches the live one.
    setSpatialLayers: async (layers): Promise<void> => {
        if (!encoder || !encoderConfig) {
            warnLog?.log('Cannot set spatial layers: not active');
            return;
        }
        if (extraLayerCountMatches(layers)) {
            debugLog?.log(`setSpatialLayers: no-op, ${extraLayerEncoders.length} extras already match request`);
            return;
        }

        infoLog?.log(`setSpatialLayers: rebuilding ${extraLayerEncoders.length} → ${layers.length} extra(s)`);
        if (extraLayerEncoders.length > 0) {
            const extras = extraLayerEncoders.slice();
            extraLayerEncoders = [];
            for (const e of extras) {
                try { await e.flush(); } catch { /* already closed */ }
                try { e.close(); } catch { /* already closed */ }
            }
        }

        // Align ladder dims to the base encoder's orientation BEFORE per-layer
        // setup. The C# / video-recorder ladder is built from sensor dims
        // (always landscape on iOS) — without the swap a portrait-oriented
        // sender ends up with portrait base + landscape extras, which makes
        // the WebGPU downscaler emit mixed-orientation frames and the receiver
        // per-keyframe verification fail (1920x1080 transmitted dims vs
        // 720x1280 decoded output on iOS Safari MSTG).
        const alignedLayers = layers.map(alignLayerToEncoderOrientation);

        // If base codec is avc1.* and incoming extras need a higher AVC level
        // than the current string admits, bump it. Without this, hot-adding a
        // 720p extra to a base whose codec was auto-corrected to avc1.64001e
        // (Level 3.0, max 720×576) would fail with NotSupportedError.
        if (encoderConfig.codec.startsWith('avc1.') && alignedLayers.length > 0) {
            let maxW = encoderConfig.width;
            let maxH = encoderConfig.height;
            for (const l of alignedLayers) {
                if (l.width > maxW) maxW = l.width;
                if (l.height > maxH) maxH = l.height;
            }
            const needed = pickAvcLevelByte(maxW, maxH);
            const currentLevel = parseInt(encoderConfig.codec.slice(-2), 16);
            if (currentLevel < needed) {
                const bumped = encoderConfig.codec.slice(0, -2) + needed.toString(16).padStart(2, '0');
                infoLog?.log(`setSpatialLayers: bumping AVC level ${encoderConfig.codec} → ${bumped} for ladder max ${maxW}x${maxH}`);
                encoderConfig.codec = bumped;
            }
        }

        const baseCodec = encoderConfig.codec;
        for (let i = 0; i < alignedLayers.length; i++) {
            const layer = alignedLayers[i];
            const layerCfg: EncoderConfig = {
                ...encoderConfig,
                width: layer.width,
                height: layer.height,
                bitrate: layer.bitrate,
                scalabilityMode: layer.scalabilityMode ?? encoderConfig.scalabilityMode,
            };
            const spatialId = i + 1;
            const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
            try { extra.initialize(); } catch { /* error already surfaced via onEncoderError */ }
            extraLayerEncoders.push(extra);
            infoLog?.log(`Simulcast layer ${spatialId} (hot): ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps codec=${baseCodec}`);
        }
        if (downscaler) downscaler.configure(currentDownscaleTargets());

        // Force a keyframe so subscribers can latch onto any newly-armed layer
        // without waiting for the next encoder-driven keyframe interval.
        nextFrameIsKeyFrame = true;
        resetRecorderHealthMetrics();
    },

    toggleBlur: async (enabled, segCfg?): Promise<void> => {
        infoLog?.log(`Toggling blur: ${enabled ? 'ON' : 'OFF'}`);
        if (enabled && !segInitialized) {
            if (!segCfg && !segConfig) throw new Error('Cannot enable blur: no segmentation config');
            const cfg = segCfg ?? segConfig!;
            await initializeSegmentation({
                ...cfg,
                outputWidth: encoderConfig?.width ?? cfg.outputWidth,
                outputHeight: encoderConfig?.height ?? cfg.outputHeight,
            });
        }
        if (segConfig) segConfig.blurEnabled = enabled;
        blurEnabled = enabled;
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    forceKeyFrame: async (): Promise<void> => { nextFrameIsKeyFrame = true; infoLog?.log('Forced next frame to be keyframe'); },

    flush: async (): Promise<void> => {
        if (encoder) { try { await encoder.flush(); infoLog?.log('Encoder flushed'); } catch (e) { warnLog?.log('Encoder flush error:', e); } }
        // Snapshot extras before iterating — switchCodec can reset the shared
        // `extraLayerEncoders` array concurrently, which would turn indexed
        // access into `undefined`.
        const extras = extraLayerEncoders.slice();
        for (let i = 0; i < extras.length; i++) {
            try { await extras[i].flush(); } catch (e) { warnLog?.log(`Extra layer ${i + 1} flush error:`, e); }
        }
    },

    stop: async (): Promise<void> => {
        infoLog?.log('Stopping video processing worker...');
        processing = false;
        streamCtx.processing = false;
        stopRecorderHealthAggregator();
        stopScreencastHeartbeat();
        stopStreamingWatchdog();

        if (streamReadLoopPromise) { try { await streamReadLoopPromise; } catch { /* ignore */ } streamReadLoopPromise = null; }
        if (previewWriter) {
            previewWriterClosed = true;
            try { await previewWriter.close(); } catch { /* writer may already be in error state */ }
            previewWriter = null;
        }
        if (inputTrack) {
            // Stop the cloned camera track explicitly — releases the camera light
            // immediately rather than waiting for MSTP/processor GC.
            try { inputTrack.stop(); } catch { /* ignore */ }
            inputTrack = null;
        }
        try { await awaitAllPendingReadbacks(); } catch { /* ignore */ }
        pendingFrame.clear();
        pendingEncoderFrame.clear();
        // Flush extras first (per-session simulcast — close fully). Primary
        // gets flushed and parked below for reuse on next start.
        // Snapshot + clear BEFORE awaiting flush — if switchCodec runs during
        // these awaits it also drains the shared array, and concurrent indexed
        // iteration would hit `undefined`. Flush and close are guarded
        // separately so a stale `flush()` rejecting with AbortError doesn't
        // skip the `close()` step.
        const extras = extraLayerEncoders.slice();
        extraLayerEncoders = [];
        for (let i = 0; i < extras.length; i++) {
            const e = extras[i];
            try { await e.flush(); } catch (err) { warnLog?.log(`Extra layer ${i + 1} flush error (stop):`, err); }
            try { e.close(); } catch (err) { warnLog?.log(`Extra layer ${i + 1} close error:`, err); }
        }
        // Park primary encoder for reuse on next start — keeps NVENC session
        // held across the stop/start gap. parkPrimaryEncoder also clears any
        // remaining extras (defensive — array should already be empty above).
        if (encoder) {
            try { await encoder.flush(); } catch (e) { warnLog?.log('Encoder flush error (stop):', e); }
            parkPrimaryEncoder();
        }
        if (videoStream) {
            videoStream.complete();
            // Wait for stream loop to finish sending remaining frames before closing connection
            try { await videoStream.whenDisposed; } catch { /* ignore */ }
            videoStream = null;
        }
        // Also await lastVideoStream if it's a different instance (e.g. codec switch created
        // a new stream while the old one was still draining). Without this, closing the peer
        // kills the connection before $sys.End is sent → "Connection is closed prematurely".
        if (lastVideoStream) {
            lastVideoStream.complete();
            try { await lastVideoStream.whenDisposed; } catch { /* ignore */ }
            lastVideoStream = null;
        }
        Api.releaseConnection('VideoCapture');
        streamCtx.rpcLiveVideoStreams = null;
        if (segInitialized) { try { outputGpuBuffer.destroy(); } catch { /* ignore */ } try { smoothedMaskBuffer.destroy(); } catch { /* ignore */ } }

        if (downscaler) { try { downscaler.dispose(); } catch { /* ignore */ } downscaler = null; }

        // Reset all state
        encoder = null; encoderConfig = null; encodersInitialized = false; onnxSession = null; segConfig = null; resolvedModelConfig = null;
        segInitialized = false; blurEnabled = false; resizeCanvas = null; resizeCtx = null;
        startTimestamp = undefined; sourceStartedAtMs = undefined; loggedI420Error = false; loggedPreConvertSkipped = false;
        slotReplacements = 0; slotArrivals = 0; lastSlotCheckTime = 0; slotPressureNotified = false;
        dimensionsReconciled = false; needsRotation = false; orientationStats = null; sourceWidth = 0; sourceHeight = 0; vadSpeaking = true; vadRemoteStreamCount = 0; vadLastPassedFrameTime = 0;
        segFrameCounter = 0; hasValidMask = false; processingFrame = false; frameSequence = 0;
        segProcessedFrames = 0; segTotalInferenceTime = 0; segTotalBlurTime = 0; segTotalProcessingTime = 0; segDroppedFrames = 0;
        videoStream = null; lastVideoStream = null; pendingStreamFrames = [];
        codecSettings = null; storedDescriptionBytesByLayer.clear();
        streamingEnabled = false; streamRecreations = 0; streamStatus = 'idle'; lastStreamError = '';
        streamProgressAt = 0; streamingStallNotified = false;
        encoderFailed = false; encoderErrorSeen = false; framesWithoutOutput = 0;
        encoderRebuildAttempted = false; encoderRebuildInFlight = false; lastEncoderRebuildAtMs = 0;

        infoLog?.log('Video processing worker stopped');
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    getStats: async (): Promise<VideoProcessingStats> => {
        const baseStats = encoder?.getStats() ?? {
            encodedFrames: 0, droppedFrames: 0, keyFrames: 0, totalBytes: 0,
            averageEncodeTime: 0, medianEncodeTime: 0, pureMedianEncodeTime: -1,
            configuredWidth: 0, configuredHeight: 0, configuredBitrate: 0, hardwareAcceleration: 'unknown',
            state: 'unconfigured' as CodecState,
            reconfigureCount: 0, replaceCount: 0,
            lastReconfigureSummary: '', lastReconfigureAgeMs: -1,
            lastErrorName: '', lastErrorMessage: '', lastErrorAgeMs: -1, errorCount: 0,
        };
        // Keep legacy aggregate counters for existing callers, but publish the
        // real per-layer encoder stats separately so diagnostics don't treat L0
        // as the whole stream.
        const spatialLayers = [
            { spatialLayerId: 0, ...baseStats },
            ...extraLayerEncoders.map((e, i) => ({ spatialLayerId: i + 1, ...e.getStats() })),
        ];
        const encoderStats = {
            ...baseStats,
            totalBytes: spatialLayers.reduce((sum, s) => sum + s.totalBytes, 0),
            encodedFrames: spatialLayers.reduce((sum, s) => sum + s.encodedFrames, 0),
            droppedFrames: spatialLayers.reduce((sum, s) => sum + s.droppedFrames, 0),
            keyFrames: spatialLayers.reduce((sum, s) => sum + s.keyFrames, 0),
            configuredBitrate: spatialLayers.reduce((sum, s) => sum + s.configuredBitrate, 0),
        };
        const segStats: SegmentationStats | null = segInitialized ? {
            processedFrames: segProcessedFrames,
            averageInferenceTime: segProcessedFrames > 0 ? segTotalInferenceTime / segProcessedFrames : 0,
            averageBlurTime: segProcessedFrames > 0 ? segTotalBlurTime / segProcessedFrames : 0,
            averageTotalTime: segProcessedFrames > 0 ? segTotalProcessingTime / segProcessedFrames : 0,
            droppedFrames: segDroppedFrames, backend: segConfig?.backend ?? 'unknown',
        } : null;
        // Pick the first non-empty error string from current stream, previous stream, or worker-level
        const streamErrors = [videoStream?.lastError, lastVideoStream?.lastError, lastStreamError];
        const streamError = streamErrors.find(e => e != null && e.length > 0) ?? '';
        const streamStats: VideoProcessingStreamingStats | null = streamingEnabled ? {
            sentFrames: videoStream?.getAddedFrameCount() ?? 0,
            pendingFrames: pendingStreamFrames.length,
            queueLength: videoStream?.getQueueLength() ?? 0,
            streamRecreations,
            status: streamStatus,
            lastError: streamError,
        } : null;
        return { encoder: encoderStats, spatialLayers, segmentation: segStats, orientation: orientationStats ? { ...orientationStats } : null, streaming: streamStats };
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    onConnectivityUpdate: async (isOnline, isConnected, isBlazorServer): Promise<void> => {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    disconnectApi: async (): Promise<void> => {
        // Debug-only path — invoked by DebugUI.disconnectApi(WorkerKind.VideoCapture).
        // Closes the WS connection; the peer's reconnect loop reopens it.
        infoLog?.log(`disconnectApi (debug): disconnecting peer`);
        try {
            if (Api.hub.defaultPeerUrl !== undefined)
                Api.hub.peers.get(Api.hub.defaultPeerUrl)?.disconnect();
        } catch (e) {
            warnLog?.log(`disconnectApi: Api not initialized`, e);
        }
    },
};
