/**
 * Decoder Worker (Universal - Chrome & Safari)
 * Handles video decoding in a dedicated worker thread using RPC communication.
 * Receives encoded chunks and outputs decoded frames via RPC callbacks.
 *
 * Used by video-player.ts for off-main-thread decoding.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type {
    DecoderWorker,
    DecoderWorkerCallbacks,
    DecoderWorkerLatencyReport,
    RawChunkMessage,
} from './decoder-worker-contract';
import { type DecoderConfig, type DecoderStats, WebCodecsDecoder } from '../webcodecs-decoder';
import {
    type DecoderCodecSelection,
    getCodecCandidates,
    selectDecoderCodec,
} from '../hevc-codec-selection';
import { getLogs } from 'logging';
import { WorkerMstgSelector } from './worker-mstg-selector';
import { BG_DRAW_INTERVAL_MS } from '../services/bg-canvas-settings';
import { BgBlurRenderer } from '../webgpu-blur';
import { Api, momentToSeconds, secondsToMoment, streamingApi } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import { initAppConstants, VIDEO } from 'app-constants';
import Denque from 'denque';
import { OwnedArrayBufferTracker } from 'buffers';
import { ServerClock } from 'server-clock';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';

const { infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

const RPC_SESSION_DEFAULT = '~';

// Worker state
let decoder: WebCodecsDecoder | null = null;
let processing = false;
let decoderConfigured = false;
let currentDecoderConfig: DecoderConfig | null = null;
// eslint-disable-next-line @typescript-eslint/no-unused-vars
let frameCount = 0;
let lastRawDescription: ArrayBuffer | null = null;

// Recovery rate-limit state. The recovery branch in decodeRawChunk creates a
// fresh WebCodecsDecoder when the live one transitions to 'closed'. Without
// limits, a flaky HW decoder loops "die → recover on next keyframe → die →
// recover" every 2-3 s, burning a fresh HW slot each time. Cooldown blocks
// recovery within 5 s of the previous attempt; the consecutive counter trips
// at 3 to surface a stream-level failure instead of recreating forever.
let lastRecoveryAtMs = 0;
let consecutiveRecoveries = 0;
const RECOVERY_COOLDOWN_MS = 5000;
const RECOVERY_MAX_ATTEMPTS = 3;

// Codec strings that have already failed `decoder.configure()` for the
// current stream. Recovery passes this set to `selectDecoderCodec` so we never
// pick a candidate we already know rejects against this stream's bitstream.
// Cleared on stream change (`stop` / `configureDecoder`).
const failedCandidates = new Set<string>();

// Track the dimensions the current decoder was last (re)configured against.
// When a keyframe arrives with larger / smaller dims, we probe whether the
// current codec string still supports the new size — only recreate the
// decoder when isConfigSupported() says no. Codec `level_idc` defines a
// max picture size, not an exact one; most layer changes within the level's
// envelope can be handled in-place by the browser decoder.
let currentCodedWidth = 0;
let currentCodedHeight = 0;
// Dim of the GOP currently being decoded — captured from decoder OUTPUT at
// emit time (`emitDecodedFrame`), not from chunk arrival. Verifier on the
// main thread compares the <video> element's videoWidth/Height to this; both
// are sourced from the same decoded frame, so they stay aligned across
// simulcast layer switches and HEVC dim-change rebuilds. Using arrival-side
// `currentCodedWidth/Height` instead would race the decoder during a tier
// change and produce false "decoded does not match latest keyframe" warnings
// that get blamed on the codec.
let lastDecodedFrameWidth = 0;
let lastDecodedFrameHeight = 0;

// Re-derive the codec string for a new keyframe description and pick the best
// HW-supported candidate. Returns null when no remaining candidate is HW-
// supported for the description (caller must end the stream). Skips candidates
// already known to fail this stream.
async function reselectCodecForDescription(
    description: ArrayBuffer,
    overrideDims?: { width: number; height: number },
): Promise<DecoderCodecSelection | null> {
    if (!currentDecoderConfig) return null;
    const candidates = getCodecCandidates(currentDecoderConfig.codec, description);
    const dims = overrideDims
        ?? ((currentDecoderConfig.codedWidth && currentDecoderConfig.codedHeight)
            ? { width: currentDecoderConfig.codedWidth, height: currentDecoderConfig.codedHeight }
            : undefined);
    return selectDecoderCodec(candidates, description, dims, failedCandidates);
}

// Probe whether the current decoder codec accepts the new frame dimensions.
// Cheap (~1 ms): VideoDecoder.isConfigSupported is the authoritative check —
// no need to hand-roll level_idc → max-pixel tables. Returns true when the
// codec string's level / profile envelope still covers the new size on this
// HW; returns false when we must reselect (escalate level) and recreate.
async function isCurrentCodecCompatible(
    width: number,
    height: number,
    description?: ArrayBuffer,
): Promise<boolean> {
    if (!currentDecoderConfig) return false;
    try {
        const config: VideoDecoderConfig = {
            codec: currentDecoderConfig.codec,
            hardwareAcceleration: 'prefer-hardware',
            codedWidth: width,
            codedHeight: height,
        };
        if (description) config.description = description;
        const { supported } = await VideoDecoder.isConfigSupported(config);
        return supported === true;
    } catch { return false; }
}

// Yield a macrotask so the platform can release a previous HW codec slot
// before allocating a new one. close() is synchronous; the slot is freed on
// the next task tick. Used between decoder close() → new WebCodecsDecoder()
// to avoid back-to-back HW slot allocations that re-trigger driver errors.
async function awaitHwReleased(): Promise<void> {
    await Promise.resolve();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
}

// Dedupe error logs by key within a 1 s window. Prevents async error cascades
// from the WebCodecs error callback flooding the inspector pipe.
const ERROR_LOG_DEDUPE_WINDOW_MS = 1000;
const errorLogLastSeenMs = new Map<string, number>();
function shouldLogDecoderError(key: string): boolean {
    const now = performance.now();
    const last = errorLogLastSeenMs.get(key) ?? 0;
    if (now - last < ERROR_LOG_DEDUPE_WINDOW_MS) return false;
    errorLogLastSeenMs.set(key, now);
    return true;
}
function decoderErrorKey(scope: string, error: unknown): string {
    if (error instanceof Error) return `${scope}:${error.name}:${error.message}`;
    if (error instanceof DOMException) return `${scope}:${error.name}:${error.message}`;
    return `${scope}:${String(error)}`;
}

const ownedArrayBufferTracker = new OwnedArrayBufferTracker();
const OWNED_ARRAY_BUFFER_LOG_INTERVAL = 300;
function getOwnedArrayBuffer(view: Uint8Array): ArrayBuffer {
    const result = ownedArrayBufferTracker.get(view);
    const stats = ownedArrayBufferTracker.stats;
    if (stats.totalCount % OWNED_ARRAY_BUFFER_LOG_INTERVAL === 0)
        infoLog?.log(`ownedArrayBuffer: fast=${stats.fastCount} ` +
            `slow=${stats.slowCount} (${(stats.fastRatio * 100).toFixed(1)}% fast)`);
    return result;
}

// Stream id of the active in-worker pull (set by startPullInWorker, cleared
// by stopPullInWorker). Used by `requestKeyframeOnDecoderError` so the
// decoder error callbacks can ask the server for a fresh keyframe instead of
// waiting for the next natural one — speeds up recovery from transient HW
// decoder failures (e.g. iOS HEVC HW intermittent EncodingError).
let activePullStreamId: string | null = null;

// Throttle PLI: at most one server-side keyframe request every 2 s.
const KEYFRAME_REQUEST_COOLDOWN_MS = 2000;
let lastKeyframeRequestAtMs = 0;

function requestKeyframeOnDecoderError(): void {
    if (!activePullStreamId) return;
    const now = performance.now();
    if (now - lastKeyframeRequestAtMs < KEYFRAME_REQUEST_COOLDOWN_MS) return;
    lastKeyframeRequestAtMs = now;
    const sid = activePullStreamId;
    warnLog?.log(`Decoder error: requesting server keyframe for ${sid}`);
    streamingApi.liveVideoStreams.RequestKeyFrame(RPC_SESSION_DEFAULT, sid)
        .catch((e: unknown) => warnLog?.log('RequestKeyFrame error:', e));
}

// Stream-based input reader loop promise (for cleanup)
let streamReadLoopPromise: Promise<void> | null = null;

// Off-thread MSTG render path: when set, decoded frames are routed into the
// selector instead of being emitted to main via onDecodedFrame.
let mstgSelector: WorkerMstgSelector | null = null;

// In-worker Fusion RPC pull state (§9). When pullActive, the worker iterates
// `streamingApi.liveVideoStreams.GetStream(...)` itself and feeds chunks into the
// decoder — main never sees per-frame work on this path.
let pullActive = false;
let pullAbortController: AbortController | null = null;
let pullStartedAtMs = 0;
let pullRetryCount = 0;
let pullSequenceNumber = 0;
let lastLatencyReportAt = 0;
let apiInitialized = false;

// Pull-loop stats. Populated only on the worker-owned pull path
// (`runPullLoop`). Surfaced via `getDecoderStatsSnapshot` so VideoPlayer's
// diagnostics can show bitrate / received frames in off-thread mode —
// otherwise main-thread `processRpcFrame` is bypassed and those counters
// stay at 0. Times use Date.now() because the worker and main-thread
// `performance.now()` have different time origins.
let pullReceivedBytes = 0;
let pullReceivedFrameCount = 0;
let pullReceivedKeyframeCount = 0;
let pullFirstFrameAt = 0;
let pullForwardedSpatialLayerId = -1;
let pullForwardedWidth = 0;
let pullForwardedHeight = 0;
let pullObservedMaxSpatialLayer = -1;

function resetPullStats(): void {
    pullReceivedBytes = 0;
    pullReceivedFrameCount = 0;
    pullReceivedKeyframeCount = 0;
    pullFirstFrameAt = 0;
    pullForwardedSpatialLayerId = -1;
    pullForwardedWidth = 0;
    pullForwardedHeight = 0;
    pullObservedMaxSpatialLayer = -1;
}

function getDecoderStatsSnapshot(): DecoderStats {
    const base: DecoderStats = decoder?.getStats() ?? {
        decodedFrames: 0,
        droppedFrames: 0,
        averageDecodeTime: 0,
        medianDecodeTime: 0,
        pureMedianDecodeTime: -1,
        decodeQueueSize: 0,
        backpressureDrops: 0,
        peakDecodeQueueSize: 0,
        lastArtifactWindowMs: 0,
        artifactWindowsCount: 0,
        hardwareAcceleration: 'unknown',
        resolution: 'N/A'
    };
    if (pullFirstFrameAt > 0) {
        const elapsedSec = (Date.now() - pullFirstFrameAt) / 1000;
        base.pullBitrateKbps = elapsedSec > 0
            ? Math.round(pullReceivedBytes * 8 / elapsedSec / 1000)
            : 0;
        base.pullReceivedBytes = pullReceivedBytes;
        base.pullReceivedFrameCount = pullReceivedFrameCount;
        base.pullReceivedKeyframeCount = pullReceivedKeyframeCount;
        base.pullForwardedSpatialLayerId = pullForwardedSpatialLayerId;
        base.pullForwardedWidth = pullForwardedWidth;
        base.pullForwardedHeight = pullForwardedHeight;
        base.pullObservedMaxSpatialLayer = pullObservedMaxSpatialLayer;
    }
    if (encodedBuffer) {
        base.encodedBufferDepth = encodedBuffer.count;
        base.encodedBufferSpanMs = encodedBuffer.durationMs;
    } else {
        base.encodedBufferDepth = 0;
        base.encodedBufferSpanMs = 0;
    }
    return base;
}

function buildLatencyReport(streamOffsetMs: number): DecoderWorkerLatencyReport {
    const ds = getDecoderStatsSnapshot();
    return {
        streamOffsetMs,
        presentedOffsetMs: mstgSelector?.getLastWrittenOffsetMs(),
        medianDecodeTimeMs: ds.pureMedianDecodeTime >= 0 ? ds.pureMedianDecodeTime : ds.medianDecodeTime,
        // Encoded pre-decode buffer is the receive-side jitter signal now;
        // decoded slot is single-frame, so adding it would just inflate
        // the depth by a constant 0–1.
        bufferDepth: ds.encodedBufferDepth ?? 0,
        bufferSpanMs: ds.encodedBufferSpanMs ?? 0,
        lastKeyframeWidth: lastDecodedFrameWidth || undefined,
        lastKeyframeHeight: lastDecodedFrameHeight || undefined,
    };
}

function ensureApiInitialized(apiUrl: string): void {
    if (apiInitialized)
        return;

    Api.init('VideoDecoder', {
        url: apiUrl,
        modules: [streamingApi],
        connectivityUI: WorkerConnectivityUI,
        sessionTokenProvider: minLifespanMs => callbacks.getSessionToken(minLifespanMs),
        requireConnection: true,
    });
    apiInitialized = true;
}

function bufferEqual(a: ArrayBuffer, b: ArrayBuffer): boolean {
    if (a.byteLength !== b.byteLength) return false;
    const viewA = new Uint8Array(a);
    const viewB = new Uint8Array(b);
    for (let i = 0; i < viewA.length; i++) {
        if (viewA[i] !== viewB[i]) return false;
    }
    return true;
}

// Diagnostic helpers — capture state at error time to root-cause iOS HEVC
// decoder failures. Updated on every chunk so error callback has context.
let lastChunkSeq = -1;
let lastChunkType: 'key' | 'delta' | '?' = '?';
let lastChunkSize = 0;
let lastChunkDescLen = 0;
let firstKeyframeLogged = false;

// Set in `initialize`/`initializeWithStreams`/`configureDecoder` whenever the
// decoder is configured with a description. Consumed by decodeRawChunk on the
// next keyframe to skip the redundant `decoder.configure()` that iOS Safari's
// HEVC HW decoder rejects with EncodingError. The check is identity-based
// (a flag), not byte-equality, so it survives RPC transports that may convert
// ArrayBuffer ↔ Uint8Array and break `instanceof` / `ArrayBuffer.isView` paths.
let initialDescriptionApplied = false;

function describeBytes(buf: AllowSharedBufferSource | undefined, maxBytes = 24): string {
    if (!buf) return '<none>';
    let view: Uint8Array;
    if (buf instanceof ArrayBuffer) {
        view = new Uint8Array(buf, 0, Math.min(maxBytes, buf.byteLength));
    } else if (ArrayBuffer.isView(buf)) {
        const v = buf as ArrayBufferView;
        view = new Uint8Array(
            v.buffer as ArrayBuffer,
            v.byteOffset,
            Math.min(maxBytes, v.byteLength));
    } else {
        return '<unknown>';
    }
    const hex: string[] = [];
    for (const b of view) {
        hex.push(b.toString(16).padStart(2, '0'));
    }
    return hex.join('');
}

// Recovery flag: set by resetDecoder / flagWaitingForKeyframe / decodeRawChunk
// to drop incoming deltas until the next keyframe arrives.
let waitingForKeyframe = false;

/**
 * Emit a decoded frame to the appropriate output (stream or RPC callback).
 */
function emitDecodedFrame(frame: VideoFrame): void {
    frameCount++;
    // Capture decoder's actual output dim — the GOP currently being rendered.
    // Reported back to main thread via DecoderWorkerLatencyReport for output
    // verification. Tracks transitions atomically (decoder rebuilds, simulcast
    // tier swaps): the <video> element's videoWidth follows the same emit, so
    // verifier's two sides stay aligned.
    if (frame.codedWidth > 0 && frame.codedHeight > 0) {
        lastDecodedFrameWidth = frame.codedWidth;
        lastDecodedFrameHeight = frame.codedHeight;
    }
    // Successful decode → clear the recovery escalation counter. Cooldown
    // (lastRecoveryAtMs) intentionally remains so we don't ping-pong fast.
    if (consecutiveRecoveries !== 0)
        consecutiveRecoveries = 0;
    if (mstgSelector) {
        mstgSelector.onDecoded(frame);
        return;
    }
    void callbacks.onDecodedFrame(frame, rpcNoWait);
}

/**
 * Helper: create WebCodecsDecoder instance with standard frame callback
 */
function createDecoder(config: DecoderConfig): WebCodecsDecoder {
    return new WebCodecsDecoder(
        { ...config, description: undefined },
        emitDecodedFrame,
        (error) => {
            errorLog?.log('Decoder error:', error);
            requestKeyframeOnDecoderError();
        }
    );
}

// In-worker pull loop. Iterates `streamingApi.liveVideoStreams.GetStream(...)`,
// feeds each frame into `serverImpl.decodeRawChunk` (which handles codec
// change, reorder, and decode), and retries with backoff on empty / error.
// Mirror of the main-thread `startPull` from video-player.ts:1265-1334.
async function runPullLoop(streamId: string, skipToMs: number): Promise<void> {
    const ac = new AbortController();
    pullAbortController = ac;
    const skipToTicks = secondsToMoment(skipToMs / 1000);
    let pullFrameCount = 0;
    let lastArrivedOffsetMs = 0;

    try {
        infoLog?.log(`pull: GetStream(${streamId}, skipTo=${skipToMs}ms)`);
        const stream = await streamingApi.liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId, skipToTicks);

        for await (const frame of stream) {
            if (ac.signal.aborted || !pullActive) break;
            pullFrameCount++;
            pullRetryCount = 0;

            const offsetMs = momentToSeconds(frame.Offset) * 1000;
            const durationMs = momentToSeconds(frame.Duration) * 1000;
            if (offsetMs > lastArrivedOffsetMs) lastArrivedOffsetMs = offsetMs;

            const data = frame.Data;
            pullReceivedBytes += data.byteLength;
            pullReceivedFrameCount++;
            if (frame.IsKeyFrame) pullReceivedKeyframeCount++;
            if (pullFirstFrameAt === 0) pullFirstFrameAt = Date.now();
            pullForwardedSpatialLayerId = frame.SpatialLayerId ?? 0;
            if (frame.MaxSpatialLayerId !== undefined && frame.MaxSpatialLayerId > pullObservedMaxSpatialLayer)
                pullObservedMaxSpatialLayer = frame.MaxSpatialLayerId;
            if (frame.Width !== undefined && frame.Width > 0)
                pullForwardedWidth = frame.Width;
            if (frame.Height !== undefined && frame.Height > 0)
                pullForwardedHeight = frame.Height;
            const dataBuffer = getOwnedArrayBuffer(data);
            let descBuffer: ArrayBuffer | undefined;
            const desc = frame.Description;
            if (desc && desc.length > 0) {
                descBuffer = getOwnedArrayBuffer(desc);
            }

            await serverImpl.decodeRawChunk(
                offsetMs * 1000,        // ms → μs
                durationMs * 1000,
                frame.IsKeyFrame,
                pullSequenceNumber++,
                frame.Width,
                frame.Height,
                dataBuffer,
                descBuffer,
            );

            const now = performance.now();
            if (now - lastLatencyReportAt > VIDEO.latencyReportIntervalMs) {
                lastLatencyReportAt = now;
                void callbacks.onLatencyReport(buildLatencyReport(lastArrivedOffsetMs), rpcNoWait);
            }
        }

        if (ac.signal.aborted || !pullActive) return;

        if (pullFrameCount > 0) {
            infoLog?.log(`pull: completed normally after ${pullFrameCount} frames`);
            pullActive = false;
            void callbacks.onPullEnded(null, rpcNoWait);
        } else {
            // Empty stream — skipTo may exceed available data, retry with backoff.
            pullRetryCount++;
            const delay = Math.min(500 * pullRetryCount, 2000);
            warnLog?.log(`pull: empty stream, retry #${pullRetryCount} in ${delay}ms`);
            setTimeout(() => {
                if (!pullActive) return;
                const retrySkipToMs = Math.max(0, ServerClock.now() - pullStartedAtMs);
                void runPullLoop(streamId, retrySkipToMs);
            }, delay);
        }
    } catch (err) {
        if (ac.signal.aborted || !pullActive) return;
        const message = err instanceof Error ? err.message : String(err);
        pullRetryCount++;
        const delay = Math.min(1000 * pullRetryCount, 5000);
        warnLog?.log(`pull: error (retry #${pullRetryCount} in ${delay}ms): ${message}`);
        setTimeout(() => {
            if (!pullActive) return;
            const retrySkipToMs = Math.max(0, ServerClock.now() - pullStartedAtMs);
            void runPullLoop(streamId, retrySkipToMs);
        }, delay);
    }
}

// ─── Encoded pre-decode buffer ──────────────────────────────────────────────
// The doc's `video buffer` (docs/video-pipeline.md) — encoded chunks held
// before the decoder. Encoded chunks are tiny (~16 KB) so we can hold many
// without the GPU/CPU memory cost of decoded frames. This is the only
// intentional playback latency on the receive side.

interface EncodedChunkArgs {
    timestamp: number;
    duration: number;
    isKeyFrame: boolean;
    sequenceNumber: number;
    width: number | undefined;
    height: number | undefined;
    data: ArrayBuffer;
    description?: ArrayBuffer;
}

class EncodedChunkBuffer {
    private readonly chunks = new Denque<EncodedChunkArgs>();

    get count(): number { return this.chunks.length; }
    get durationUs(): number { return this.getDurationUs(); }
    get durationMs(): number { return this.durationUs / 1000; }

    clear(): void {
        this.chunks.clear();
    }

    push(args: EncodedChunkArgs): void {
        this.chunks.push(args);
        this.trimExcess();
    }

    shiftReady(): EncodedChunkArgs | undefined {
        if (this.getDurationUs() < getTargetBufferDurationUs())
            return undefined;
        return this.chunks.shift();
    }

    private trimExcess(): void {
        if (this.getDurationUs() <= getTargetBufferDurationUs())
            return;

        const dropCount = this.findSkippablePrefixSize();
        if (dropCount > 0)
            this.chunks.remove(0, dropCount);
    }

    private findSkippablePrefixSize(): number {
        const count = this.chunks.length;
        if (count < 2)
            return 0;

        const last = this.chunks.peekBack();
        if (!last)
            return 0;

        const targetUs = getTargetBufferDurationUs();
        const endUs = getChunkEndUs(last);
        let bestDropCount = 0;

        // Safe catch-up point: the first kept chunk must be a keyframe, and
        // the remaining media duration must still cover the target buffer.
        for (let i = 1; i < count; i++) {
            const chunk = this.chunks.peekAt(i);
            if (!chunk?.isKeyFrame)
                continue;

            if (endUs - chunk.timestamp > targetUs)
                bestDropCount = i;
        }

        return bestDropCount;
    }

    private getDurationUs(): number {
        const first = this.chunks.peekFront();
        const last = this.chunks.peekBack();
        if (!first || !last)
            return 0;

        return Math.max(0, getChunkEndUs(last) - first.timestamp);
    }
}

function getTargetBufferDurationUs(): number {
    return VIDEO.targetBufferDurationMs * 1000;
}

function getChunkEndUs(chunk: EncodedChunkArgs): number {
    return chunk.timestamp + getChunkDurationUs(chunk);
}

function getChunkDurationUs(chunk: EncodedChunkArgs): number {
    return chunk.duration > 0
        ? chunk.duration
        : VIDEO.frameDurationMs * 1000;
}

// Mirror of webcodecs-decoder's BackpressureQueueLimit. Keep in sync.
const DRAIN_DECODE_QUEUE_LIMIT = 4;
// How long to back off when the decoder's internal queue is full.
// One frame at 30 fps is ~33 ms; 5 ms keeps polling responsive without
// busy-spinning.
const DRAIN_BACKOFF_MS = 5;

let encodedBuffer: EncodedChunkBuffer | null = null;
let drainRunning = false;

function getEncodedBuffer(): EncodedChunkBuffer {
    if (encodedBuffer) return encodedBuffer;
    // Lazy because VIDEO is populated only after the init RPC arrives.
    encodedBuffer = new EncodedChunkBuffer();
    return encodedBuffer;
}

function clearEncodedBuffer(): void {
    encodedBuffer?.clear();
}

function pushEncodedChunk(args: EncodedChunkArgs): void {
    const buf = getEncodedBuffer();
    buf.push(args);
    triggerDrain();
}

function triggerDrain(): void {
    if (drainRunning) return;
    drainRunning = true;
    void drainEncodedBuffer().finally(() => { drainRunning = false; });
}

async function drainEncodedBuffer(): Promise<void> {
    while (encodedBuffer && encodedBuffer.count > 0) {
        const dec = decoder;
        if (dec && dec.getDecodeQueueSize() >= DRAIN_DECODE_QUEUE_LIMIT) {
            await new Promise<void>(r => setTimeout(r, DRAIN_BACKOFF_MS));
            continue;
        }
        const args = encodedBuffer.shiftReady();
        if (!args)
            return;

        try {
            await processEncodedChunk(
                args.timestamp, args.duration, args.isKeyFrame, args.sequenceNumber,
                args.width, args.height, args.data, args.description);
        } catch (e) {
            errorLog?.log('Drain decode failed:', e);
        }
    }
}

/**
 * Decode raw encoded bytes (used by video-player.ts for off-main-thread decoding).
 * Creates EncodedVideoChunk internally from raw bytes.
 *
 * Called from both the decodeRawChunk RPC entry (via pushEncodedChunk) and
 * the in-worker drain loop. Module-scoped state is shared.
 */
async function processEncodedChunk(
    timestamp: number,
    duration: number,
    isKeyFrame: boolean,
    sequenceNumber: number,
    width: number | undefined,
    height: number | undefined,
    data: ArrayBuffer,
    description?: ArrayBuffer
): Promise<void> {
    if (!decoder || !processing) {
        return;
    }

    // Gate after reset/recovery: drop deltas until first keyframe arrives.
    // resetDecoder sets waitingForKeyframe=true; without this gate an
    // in-flight chunk from the prior stream feeds the fresh decoder and
    // triggers "A key frame is required after configure()" — pixelation
    // until the next IDR.
    if (waitingForKeyframe) {
        if (!isKeyFrame) {
            return;
        }
        infoLog?.log(`Recovery keyframe received: seq=${sequenceNumber}`);
        waitingForKeyframe = false;
    }

    // Diagnostic: snapshot what the decoder is about to be fed so the
    // error callback can reference it. Capture `dataLen` before the
    // EncodedVideoChunk constructor (which uses `transfer: [data]` and
    // detaches the buffer — `data.byteLength` reads 0 after that).
    const dataLen = data.byteLength;
    lastChunkSeq = sequenceNumber;
    lastChunkType = isKeyFrame ? 'key' : 'delta';
    lastChunkSize = dataLen;
    lastChunkDescLen = description?.byteLength ?? 0;

    // Diagnostic: on the first keyframe of a fresh stream, log a side-by-side
    // comparison of the description from VideoStreamInfo metadata (used at
    // initialize) vs the description on this keyframe. iOS Safari HEVC HW
    // decoder errors implicate a possible mismatch — confirm or rule out.
    if (isKeyFrame && !firstKeyframeLogged) {
        firstKeyframeLogged = true;
        const initDesc = currentDecoderConfig?.description;
        const initLen = initDesc?.byteLength ?? 0;
        const chunkLen = description?.byteLength ?? 0;
        const initHex = describeBytes(initDesc);
        const chunkHex = describeBytes(description);
        const dataHex = describeBytes(data);
        let initVsChunk: string;
        if (initLen === chunkLen && initDesc && description) {
            let initAsArrayBuffer: ArrayBuffer;
            if (initDesc instanceof ArrayBuffer) {
                initAsArrayBuffer = initDesc;
            } else if (ArrayBuffer.isView(initDesc)) {
                const v = initDesc as ArrayBufferView;
                initAsArrayBuffer = (v.buffer as ArrayBuffer).slice(
                    v.byteOffset, v.byteOffset + v.byteLength);
            } else {
                initAsArrayBuffer = new ArrayBuffer(0);
            }
            initVsChunk = bufferEqual(initAsArrayBuffer, description) ? 'EQUAL' : 'DIFFER';
        } else {
            initVsChunk = initLen === 0 ? 'init-no-desc' : 'len-differ';
        }
        infoLog?.log(
            `processEncodedChunk: first-keyframe seq=${sequenceNumber}, dataLen=${dataLen}, ` +
            `initDescLen=${initLen}, chunkDescLen=${chunkLen}, cmp=${initVsChunk}, ` +
            `decoderState=${decoder.getState()}, decoderConfigured=${decoderConfigured}, ` +
            `codec=${currentDecoderConfig?.codec}, hwAccel=${currentDecoderConfig?.hardwareAcceleration}`);
        infoLog?.log(`processEncodedChunk: first-keyframe initDescHex=${initHex}`);
        infoLog?.log(`processEncodedChunk: first-keyframe chunkDescHex=${chunkHex}`);
        infoLog?.log(`processEncodedChunk: first-keyframe dataHex=${dataHex}`);
    }

    try {
        // If we have a description and it's a keyframe, reconfigure the decoder only if description changed
        if (isKeyFrame && description && description.byteLength > 0) {
            if (!decoderConfigured) {
                // First keyframe of a fresh stream. The VideoDecoder built at
                // initialize() has been sitting unconfigured for hundreds of ms
                // while RPC handshake + GetVideo roundtripped; on iOS Safari
                // HEVC HW that stale instance can no longer be configured into
                // a working state — first decode fails with
                // "EncodingError: Decoder failure" even though state reads
                // 'configured'. Recovery branch below already proves
                // fresh-decoder + initialize(description) + decode() is
                // reliable; mirror it here.
                if (decoder.getState() !== 'closed') {
                    decoder.close();
                }
                // Re-derive codec from THIS keyframe's description (ground
                // truth from the bitstream). Init-time codec was a hint; the
                // keyframe SPS may say a different tier/level/profile that
                // forces a different codec string.
                const selection = await reselectCodecForDescription(description);
                if (!selection) {
                    errorLog?.log(`No HW-supported codec for first keyframe ` +
                        `description (descLen=${description.byteLength}, ` +
                        `hex=${describeBytes(description)}); ending stream`);
                    if (pullActive) {
                        pullActive = false;
                        void callbacks.onPullEnded(
                            'codec not supported for stream description', rpcNoWait);
                    }
                    return;
                }
                if (selection.codec !== currentDecoderConfig!.codec) {
                    warnLog?.log(`First keyframe codec swap: ${
                        currentDecoderConfig!.codec} -> ${selection.codec}`);
                    currentDecoderConfig = { ...currentDecoderConfig!, codec: selection.codec };
                }
                const freshConfig: DecoderConfig = { ...currentDecoderConfig!, description };
                decoder = new WebCodecsDecoder(
                    freshConfig,
                    emitDecodedFrame,
                    (error) => {
                        if (shouldLogDecoderError(decoderErrorKey('first-kf', error)))
                            errorLog?.log(`Decoder error (fresh first-keyframe path, state=${decoder?.getState() ?? '?'}, ` +
                                `lastChunkSeq=${lastChunkSeq}, lastChunkType=${lastChunkType}, ` +
                                `lastChunkBytes=${lastChunkSize}, lastDescLen=${lastChunkDescLen}):`, error);
                        requestKeyframeOnDecoderError();
                    }
                );
                decoder.initialize();
                lastRawDescription = description.slice(0);
                initialDescriptionApplied = false;
                warnLog?.log(`processEncodedChunk: built fresh decoder for first keyframe, ` +
                    `codec=${currentDecoderConfig!.codec}, ` +
                    `descLen=${description.byteLength}, decoderState=${decoder.getState()}`);
            } else if (initialDescriptionApplied) {
                // Decoder was already configured with description at init/configure path —
                // skip the redundant decoder.configure() that iOS Safari HEVC HW
                // rejects with EncodingError even when bytes are identical.
                initialDescriptionApplied = false;
                lastRawDescription = description.slice(0);
                warnLog?.log(`[FLAG_PATH] First keyframe: skipped redundant configure, ` +
                    `seeded lastRawDescription (${description.byteLength} bytes), ` +
                    `decoderState=${decoder.getState()}`);
            } else if (!lastRawDescription || !bufferEqual(lastRawDescription, description)) {
                warnLog?.log(`updateDescription firing: lastRawDesc=${
                    lastRawDescription ? lastRawDescription.byteLength : 'null'} bytes, ` +
                    `chunkDesc=${description.byteLength} bytes, decoderState=${decoder.getState()}`);
                // Re-derive codec from the new description — simulcast layer
                // switches carry a new SPS with different tier/level. If the
                // codec string changes, swap it atomically with the new
                // description.
                const selection = await reselectCodecForDescription(description);
                if (!selection) {
                    errorLog?.log(`No HW-supported codec for new keyframe ` +
                        `description (layer switch?); ending stream`);
                    if (pullActive) {
                        pullActive = false;
                        void callbacks.onPullEnded(
                            'codec not supported for new stream description', rpcNoWait);
                    }
                    return;
                }
                if (selection.codec !== currentDecoderConfig!.codec) {
                    // Codec string changed (level / tier / profile escalation
                    // from new SPS). Close + recreate — HW slots are pinned
                    // to the original codec capability and in-place
                    // configure() can't always cross that boundary cleanly.
                    const oldCodec = currentDecoderConfig!.codec;
                    warnLog?.log(`Layer-switch codec swap (close+recreate): ${
                        oldCodec} -> ${selection.codec}`);
                    await awaitHwReleased();
                    if (decoder.getState() !== 'closed') decoder.close();
                    mstgSelector?.resetPrimming();
                    currentDecoderConfig = { ...currentDecoderConfig!, codec: selection.codec };
                    const freshConfig: DecoderConfig = { ...currentDecoderConfig, description };
                    decoder = new WebCodecsDecoder(
                        freshConfig,
                        emitDecodedFrame,
                        (error) => {
                            if (shouldLogDecoderError(decoderErrorKey('layer-switch', error)))
                                errorLog?.log(`Decoder error (layer-switch path, state=${
                                    decoder?.getState() ?? '?'}):`, error);
                            requestKeyframeOnDecoderError();
                        }
                    );
                    decoder.initialize();
                } else {
                    // Same codec, only SPS bytes differ (minor variation,
                    // e.g. different VUI). In-place reconfigure is fine.
                    // Forward the new dims so configure() picks up the new
                    // SPS conformance window — without this, Chromium's
                    // HEVC HW decoder keeps applying the old crop.
                    decoder.updateDescription(description, selection.codec, width, height);
                    mstgSelector?.resetPrimming();
                    if (width && height) {
                        currentDecoderConfig = {
                            ...currentDecoderConfig!,
                            codedWidth: width,
                            codedHeight: height,
                        };
                        currentCodedWidth = width;
                        currentCodedHeight = height;
                    }
                }
                lastRawDescription = description.slice(0);
                infoLog?.log('Description changed, decoder reconfigured');
            }
            decoderConfigured = true;
        } else if (isKeyFrame && !decoderConfigured && currentDecoderConfig?.description) {
            // Recreate decoder with description to avoid double-configure.
            // Handles skipTo jumping past the first keyframe with per-frame SPS/PPS.
            warnLog?.log(`Recreating decoder with initial description for skipTo keyframe, ` +
                `descLen=${currentDecoderConfig.description.byteLength}, ` +
                `descHex=${describeBytes(currentDecoderConfig.description)}`);
            if (decoder.getState() !== 'closed') {
                decoder.close();
            }
            decoder = new WebCodecsDecoder(
                currentDecoderConfig,
                emitDecodedFrame,
                (error) => {
                    if (shouldLogDecoderError(decoderErrorKey('recreate', error)))
                        errorLog?.log(`Decoder error (recreate path, state=${decoder?.getState() ?? '?'}):`, error);
                    requestKeyframeOnDecoderError();
                }
            );
            decoder.initialize();
            decoderConfigured = true;
        }

        // For AV1, we don't need a description — mark as configured on first keyframe
        if (isKeyFrame && !decoderConfigured) {
            const isAV1 = currentDecoderConfig?.codec.startsWith('av01');
            if (isAV1 || !currentDecoderConfig?.description) {
                decoderConfigured = true;
            }
        }

        // Resolution-change handling. Fires on keyframes that carry
        // VideoFrameDto.Width/Height (any codec — AVC Annex B, AV1, VP9
        // and HEVC streams alike). The codec's `level_idc` defines a max
        // picture size; rotation, simulcast layer changes, or screencast
        // window resizes commonly stay within that envelope and the
        // browser decoder adapts internally — no need to recreate.
        //
        // Probe authority: `VideoDecoder.isConfigSupported(codec, dims, desc?)`
        // tells us directly whether the current codec config still accepts
        // the new size. Only when it returns false do we reselect (escalate
        // level / profile) and close+create a fresh decoder.
        if (isKeyFrame && width && height) {
            if (currentCodedWidth === 0 || currentCodedHeight === 0) {
                // First keyframe with dims after init — just track. Decoder
                // was already (re)built above with the init-time codec.
                currentCodedWidth = width;
                currentCodedHeight = height;
            } else if (width !== currentCodedWidth || height !== currentCodedHeight) {
                const compatible = await isCurrentCodecCompatible(width, height, description);
                if (compatible) {
                    infoLog?.log(`Resolution change ${currentCodedWidth}x${currentCodedHeight} ` +
                        `-> ${width}x${height}; codec ${currentDecoderConfig?.codec} compatible, ` +
                        `decoder adapts in-place`);
                    currentCodedWidth = width;
                    currentCodedHeight = height;
                    currentDecoderConfig = {
                        ...currentDecoderConfig!,
                        codedWidth: width,
                        codedHeight: height,
                    };
                    // Some browser decoders keep the previous coded/display
                    // size across an Annex-B layer switch even though the
                    // keyframe carries the new SPS in-band. Force a fresh
                    // configure on every no-description dimension change:
                    // updateDescription if we have bytes, otherwise rebuild
                    // with the new codedWidth/codedHeight before feeding this KF.
                    if (description) {
                        decoder.updateDescription(description, undefined, width, height);
                        mstgSelector?.resetPrimming();
                        lastRawDescription = description.slice(0);
                    } else {
                        await awaitHwReleased();
                        if (decoder.getState() !== 'closed') decoder.close();
                        mstgSelector?.resetPrimming();
                        const freshConfig: DecoderConfig = { ...currentDecoderConfig };
                        decoder = new WebCodecsDecoder(
                            freshConfig,
                            emitDecodedFrame,
                            (error) => {
                                if (shouldLogDecoderError(decoderErrorKey('dim-change-hevc', error)))
                                    errorLog?.log(`Decoder error (HEVC dim-change rebuild path, state=${
                                        decoder?.getState() ?? '?'}):`, error);
                                requestKeyframeOnDecoderError();
                            }
                        );
                        decoder.initialize();
                        warnLog?.log(`Dim-change without description bytes; rebuilt decoder ` +
                            `@ ${width}x${height} to refresh coded size`);
                    }
                } else {
                    const oldCodec = currentDecoderConfig!.codec;
                    warnLog?.log(`Resolution change ${currentCodedWidth}x${currentCodedHeight} ` +
                        `-> ${width}x${height} incompatible with ${oldCodec}; reselecting`);
                    const candidates = getCodecCandidates(oldCodec, description);
                    const selection = await selectDecoderCodec(
                        candidates, description, { width, height }, failedCandidates);
                    if (!selection) {
                        errorLog?.log(`No HW-supported codec for ${width}x${height}; ending stream`);
                        if (pullActive) {
                            pullActive = false;
                            void callbacks.onPullEnded(
                                'codec not supported for new dims', rpcNoWait);
                        }
                        return;
                    }
                    await awaitHwReleased();
                    if (decoder.getState() !== 'closed') decoder.close();
                    mstgSelector?.resetPrimming();
                    currentDecoderConfig = {
                        ...currentDecoderConfig!,
                        codec: selection.codec,
                        codedWidth: width,
                        codedHeight: height,
                    };
                    const freshConfig: DecoderConfig = description
                        ? { ...currentDecoderConfig, description }
                        : { ...currentDecoderConfig, description: undefined };
                    decoder = new WebCodecsDecoder(
                        freshConfig,
                        emitDecodedFrame,
                        (error) => {
                            if (shouldLogDecoderError(decoderErrorKey('dim-change', error)))
                                errorLog?.log(`Decoder error (dim-change path, state=${
                                    decoder?.getState() ?? '?'}):`, error);
                            requestKeyframeOnDecoderError();
                        }
                    );
                    decoder.initialize();
                    currentCodedWidth = width;
                    currentCodedHeight = height;
                    if (description) lastRawDescription = description.slice(0);
                    warnLog?.log(`Dim-change rebuild: ${oldCodec} -> ${selection.codec} ` +
                        `@ ${width}x${height}`);
                }
            }
        }

        // Defense-in-depth: never feed empty data to the decoder
        if (dataLen === 0) {
            warnLog?.log(`Skipping chunk with empty data: seq=${sequenceNumber}, isKey=${isKeyFrame}`);
            return;
        }

        // Create EncodedVideoChunk from raw bytes. `transfer: [data]` detaches
        // the source ArrayBuffer into the chunk — skips the constructor's
        // implicit byte copy. Per WebCodecs 2023+; safe because `data` is
        // not read after this point. The cast is needed because the TS
        // dom lib hasn't yet added the `transfer` field.
        const chunk = new EncodedVideoChunk({
            type: isKeyFrame ? 'key' : 'delta',
            timestamp,
            duration,
            data,
            transfer: [data],
        } as EncodedVideoChunkInit & { transfer?: ArrayBuffer[] });

        // Check decoder state — recover from closed/error state on keyframe
        if (decoder.getState() !== 'configured') {
            if (isKeyFrame && currentDecoderConfig) {
                // Recovery cooldown: skip recreation if we already attempted
                // one within RECOVERY_COOLDOWN_MS. Without this gate, a HW
                // decoder that fails every ~3 s would be rebuilt on every
                // keyframe — burning a fresh HW slot per attempt and
                // staying broken because the platform hasn't fully released
                // the previous slot yet.
                const nowMs = Date.now();
                if (nowMs - lastRecoveryAtMs < RECOVERY_COOLDOWN_MS) {
                    warnLog?.log(`Recovery skipped (cooldown ${
                        RECOVERY_COOLDOWN_MS}ms): seq=${sequenceNumber}, ` +
                        `${nowMs - lastRecoveryAtMs}ms since last attempt`);
                    return;
                }
                // After RECOVERY_MAX_ATTEMPTS consecutive failed recoveries,
                // surface a stream-level failure instead of recreating
                // forever. The receiver UI can re-pull cleanly via skipTo
                // (the existing app-layer recovery), and a clean failure
                // beats an endless decoder churn.
                if (consecutiveRecoveries >= RECOVERY_MAX_ATTEMPTS) {
                    errorLog?.log(`Recovery gave up after ${consecutiveRecoveries} ` +
                        `consecutive attempts; reporting stream end`);
                    if (pullActive) {
                        pullActive = false;
                        void callbacks.onPullEnded(
                            'decoder unrecoverable after retries',
                            rpcNoWait);
                    }
                    return;
                }
                consecutiveRecoveries++;
                lastRecoveryAtMs = nowMs;
                mstgSelector?.resetPrimming();
                // HEVC/AVC require description on every configure() — recovery must re-apply
                // the cached description, otherwise the next keyframe fails with
                // "A key frame is required after configure()" DataError.
                const recoveryDescription: ArrayBuffer | undefined = description && description.byteLength > 0
                    ? description
                    : (lastRawDescription ?? undefined);
                // Mark the codec that just failed as exhausted for this
                // stream, then re-select against the recovery description.
                // Without this, recovery would loop on the same broken codec
                // string until RECOVERY_MAX_ATTEMPTS trips and the stream
                // ends — even though a different candidate (e.g. opposite
                // tier) would have configured cleanly.
                const failedCodec = currentDecoderConfig.codec;
                failedCandidates.add(failedCodec);
                let recoveryCodec = failedCodec;
                if (recoveryDescription) {
                    const selection = await reselectCodecForDescription(recoveryDescription);
                    if (!selection) {
                        errorLog?.log(`Recovery: no remaining HW-supported codec ` +
                            `(failed=[${[...failedCandidates].join(', ')}]); ending stream`);
                        if (pullActive) {
                            pullActive = false;
                            void callbacks.onPullEnded(
                                'codec exhausted', rpcNoWait);
                        }
                        return;
                    }
                    recoveryCodec = selection.codec;
                    if (recoveryCodec !== failedCodec) {
                        warnLog?.log(`Recovery codec swap: ${failedCodec} -> ${recoveryCodec}`);
                        currentDecoderConfig = { ...currentDecoderConfig, codec: recoveryCodec };
                    }
                }
                warnLog?.log(`Decoder in state '${decoder.getState()}', recovering (attempt ${
                    consecutiveRecoveries}/${RECOVERY_MAX_ATTEMPTS}) on keyframe seq=${
                    sequenceNumber}, dataLen=${dataLen}, descLen=${
                    recoveryDescription?.byteLength ?? 0}, descHex=${
                    describeBytes(recoveryDescription)}, codec=${recoveryCodec}, source=${
                    description && description.byteLength > 0 ? 'chunk' : 'cached'}`);
                try {
                    // Yield a macrotask so the platform can release the
                    // previous HW slot before we allocate a new one.
                    await awaitHwReleased();
                    const recoveryConfig: DecoderConfig = recoveryDescription
                        ? { ...currentDecoderConfig, description: recoveryDescription }
                        : { ...currentDecoderConfig, description: undefined };
                    decoder = new WebCodecsDecoder(
                        recoveryConfig,
                        emitDecodedFrame,
                        (error) => {
                            if (shouldLogDecoderError(decoderErrorKey('recovery', error)))
                                errorLog?.log(`Decoder error (recovery path, state=${decoder?.getState() ?? '?'}):`, error);
                            requestKeyframeOnDecoderError();
                        }
                    );
                    decoder.initialize();
                    decoderConfigured = true;
                    if (recoveryDescription) {
                        lastRawDescription = recoveryDescription.slice(0);
                    }
                } catch (recoveryError) {
                    errorLog?.log('Decoder recovery failed:', recoveryError);
                    return;
                }
            } else {
                // Can't recover on delta frame — need keyframe
                return;
            }
        }

        if (isKeyFrame) {
            infoLog?.log(`processEncodedChunk: pre-decode keyframe seq=${sequenceNumber}, ` +
                `state=${decoder.getState()}, configured=${decoderConfigured}, ` +
                `descLen=${description?.byteLength ?? 0}, dataLen=${dataLen}, ` +
                `flagWasUsed=${!initialDescriptionApplied && sequenceNumber === 0 ? 'maybe' : 'n/a'}`);
        }

        // Decode using the WebCodecsDecoder wrapper (tracks timing for diagnostics)
        decoder.decodeRaw(chunk);
    } catch (error) {
        errorLog?.log('Error decoding raw chunk:', error);
    }
}

// RPC Server Implementation
const serverImpl: DecoderWorker = {
    ...sharedSettingsWorker,

    init: async (appConstants, sharedSettings: SharedSettingsSnapshot): Promise<void> => {
        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
    },

    /**
   * Initialize the decoder
   */
    // eslint-disable-next-line
    initialize: async (config): Promise<void> => {
        try {
            infoLog?.log('Initializing decoder for codec:', config.codec,
                ', descriptionLen:', config.description
                    ? config.description.byteLength
                    : 'none');

            currentDecoderConfig = config;
            currentCodedWidth = config.codedWidth ?? 0;
            currentCodedHeight = config.codedHeight ?? 0;

            // Defer decoder.configure() to the first keyframe. iOS Safari HEVC HW
            // decoder loses state during the idle gap between init's configure()
            // and first decode (WS handshake + server roundtrip can be hundreds
            // of ms). Recovery branch already proves single-configure-then-decode
            // works — match that pattern from the start.
            decoder = new WebCodecsDecoder(
                { ...config, description: undefined },
                emitDecodedFrame,
                (error) => {
                    if (shouldLogDecoderError(decoderErrorKey('initialize', error)))
                        errorLog?.log(`Decoder error (state=${decoder?.getState() ?? '?'}, ` +
                            `lastChunkSeq=${lastChunkSeq}, lastChunkType=${lastChunkType}, ` +
                            `lastChunkBytes=${lastChunkSize}, lastDescLen=${lastChunkDescLen}):`, error);
                    requestKeyframeOnDecoderError();
                }
            );
            // Don't call decoder.initialize() — keep state 'unconfigured' until
            // first keyframe applies its own description via updateDescription.
            decoderConfigured = false;
            initialDescriptionApplied = false;

            processing = true;
            infoLog?.log(`processEncodedChunk: decoder created, configure() deferred to first keyframe, ` +
                `codec=${config.codec}, hwAccel=${config.hardwareAcceleration}, ` +
                `descLen=${config.description ? config.description.byteLength : 0}, ` +
                `dims=${currentCodedWidth}x${currentCodedHeight}, ` +
                `decoderState=${decoder.getState()}`);
        } catch (error) {
            errorLog?.log('Failed to initialize decoder:', error);
            throw error;
        }
    },

    /**
   * Initialize and start stream-based decoding.
   */
    // eslint-disable-next-line @typescript-eslint/require-await
    initializeWithStreams: async (
        config: DecoderConfig,
        chunkInputStream: ReadableStream<RawChunkMessage>,
    ): Promise<void> => {
        try {
            infoLog?.log('Initializing decoder (stream input, RPC output) for codec:', config.codec);

            currentDecoderConfig = config;
            currentCodedWidth = config.codedWidth ?? 0;
            currentCodedHeight = config.codedHeight ?? 0;

            // Output goes via RPC callback (onDecodedFrame) — no stream output writer.
            // Cross-worker VideoFrame transfer via postMessage+transfer works correctly,
            // unlike WritableStream which uses structured clone.

            // Defer configure() to first keyframe — see initialize() comment above.
            decoder = new WebCodecsDecoder(
                { ...config, description: undefined },
                emitDecodedFrame,
                (error) => {
                    if (shouldLogDecoderError(decoderErrorKey('initWithStreams', error)))
                        errorLog?.log(`Decoder error (state=${decoder?.getState() ?? '?'}, ` +
                            `lastChunkSeq=${lastChunkSeq}, lastChunkType=${lastChunkType}, ` +
                            `lastChunkBytes=${lastChunkSize}, lastDescLen=${lastChunkDescLen}):`, error);
                    requestKeyframeOnDecoderError();
                }
            );
            decoderConfigured = false;
            initialDescriptionApplied = false;
            infoLog?.log(`processEncodedChunk: decoder created (stream), configure() deferred to first keyframe, ` +
                `codec=${config.codec}, descLen=${config.description ? config.description.byteLength : 0}, ` +
                `decoderState=${decoder.getState()}`);

            processing = true;

            // Start reading from input stream (async, runs in background)
            const inputReader = chunkInputStream.getReader();
            streamReadLoopPromise = (async () => {
                try {
                    while (processing) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                        const { done, value } = await inputReader.read();
                        if (done) {
                            infoLog?.log('Decoder stream input ended');
                            break;
                        }
                        // Reuse the existing decodeRawChunk logic
                        await serverImpl.decodeRawChunk(
                            value.timestamp,
                            value.duration,
                            value.isKeyFrame,
                            value.sequenceNumber,
                            value.width,
                            value.height,
                            value.data,
                            value.description
                        );
                    }
                } catch (error) {
                    if (processing) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                        errorLog?.log('Decoder stream read error:', error);
                    }
                } finally {
                    try { inputReader.releaseLock(); } catch { /* ignore */ }
                }
            })();

            infoLog?.log('Ready to decode chunks (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to initialize decoder stream mode:', error);
            throw error;
        }
    },

    /**
   * Stop the decoder
   */
    stop: async (): Promise<void> => {
        try {
            infoLog?.log('Stopping decoder...');

            processing = false;
            decoderConfigured = false;
            lastRecoveryAtMs = 0;
            consecutiveRecoveries = 0;
            failedCandidates.clear();
            currentCodedWidth = 0;
            currentCodedHeight = 0;

            if (pullActive) {
                pullActive = false;
                if (pullAbortController) {
                    pullAbortController.abort();
                    pullAbortController = null;
                }
            }

            if (mstgSelector) {
                mstgSelector.dispose();
                mstgSelector = null;
            }

            // Wait for stream read loop to finish
            if (streamReadLoopPromise) {
                try { await streamReadLoopPromise; } catch { /* ignore */ }
                streamReadLoopPromise = null;
            }

            // Wait for in-flight chunks
            await new Promise(resolve => setTimeout(resolve, 200));

            // Flush and close decoder
            if (decoder) {
                try {
                    await decoder.flush();
                    decoder.close();
                    infoLog?.log('Decoder closed');
                } catch (error) {
                    warnLog?.log('Decoder close error:', error);
                }
            }

            infoLog?.log('Decoder stopped');

            // Reset state
            decoder = null;
            currentDecoderConfig = null;
            frameCount = 0;
            lastRawDescription = null;
            firstKeyframeLogged = false;
            lastChunkSeq = -1;
            lastChunkType = '?';
            lastChunkSize = 0;
            lastChunkDescLen = 0;
            waitingForKeyframe = false;
            initialDescriptionApplied = false;
            clearEncodedBuffer();
        } catch (error) {
            errorLog?.log('Failed to stop decoder:', error);
            throw error;
        }
    },

    /**
     * Decode raw encoded bytes (used by video-player.ts for off-main-thread decoding).
     *
     * Pushes the chunk into the encoded pre-decode buffer. The drain loop
     * pulls from the buffer at the decoder's actual throughput, gated by
     * VideoDecoder.decodeQueueSize. Returns immediately so back-pressure
     * from a slow decoder absorbs into the buffer (with KF-aware eviction)
     * rather than blocking the producer.
     */
    // eslint-disable-next-line @typescript-eslint/require-await
    decodeRawChunk: async (
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        sequenceNumber: number,
        width: number | undefined,
        height: number | undefined,
        data: ArrayBuffer,
        description?: ArrayBuffer
    ): Promise<void> => {
        if (!processing) return;
        pushEncodedChunk({
            timestamp, duration, isKeyFrame, sequenceNumber,
            width, height, data, description,
        });
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    flagWaitingForKeyframe: async (): Promise<void> => {
        // Drop incoming deltas at decodeRawChunk's gate until the next key
        // arrives in the existing stream. Does NOT touch the decoder — keeps
        // it alive so it can consume the recovery keyframe normally.
        waitingForKeyframe = true;
        infoLog?.log('flagWaitingForKeyframe: gate armed');
    },

    /**
     * Reset the decoder (flush internal queue).
     * Used as last-resort recovery on real decoder errors.
     */
    // eslint-disable-next-line
    resetDecoder: async (): Promise<void> => {
        if (!decoder) return;

        try {
            infoLog?.log('Resetting decoder');

            // Close existing decoder
            if (decoder.getState() !== 'closed') {
                decoder.close();
            }

            // Recreate decoder
            if (currentDecoderConfig) {
                decoder = createDecoder(currentDecoderConfig);
                decoder.initialize();
                decoderConfigured = false;
                // createDecoder strips description, so the decoder is configured
                // codec-only here. Clear the flag so the next keyframe DOES run
                // updateDescription to apply the per-frame SPS/PPS.
                initialDescriptionApplied = false;
                // Drop deltas until a key arrives. Without this, an in-flight
                // chunk from the pre-reset stream can race the new decoder and
                // trigger "A key frame is required after configure()".
                waitingForKeyframe = true;
                // Drop any chunks queued in the encoded buffer behind the old
                // decoder — they belong to the pre-reset stream's GOP.
                clearEncodedBuffer();
                infoLog?.log('Decoder reset complete');
            }
        } catch (error) {
            errorLog?.log('Error resetting decoder:', error);
        }
    },

    /**
     * Reconfigure the decoder with new config.
     * Used after reset for tab visibility restore.
     */
    // eslint-disable-next-line
    configureDecoder: async (config: DecoderConfig): Promise<void> => {
        try {
            infoLog?.log('Configuring decoder with:', config.codec);
            currentDecoderConfig = config;
            currentCodedWidth = config.codedWidth ?? 0;
            currentCodedHeight = config.codedHeight ?? 0;
            // New stream description → forget any candidate failures from the
            // previous stream; the new bitstream may accept what the old one
            // rejected (different encoder, different SPS, etc.).
            failedCandidates.clear();
            // Pre-decode buffer holds chunks from the OLD config — drop them.
            clearEncodedBuffer();

            if (decoder && decoder.getState() !== 'closed') {
                decoder.close();
            }

            if (config.description) {
                // Single configure() in AVCC mode — same pattern as initialize().
                // Do NOT use createDecoder() which strips description, causing
                // double-configure (Annex B → AVCC) that breaks Chrome's VideoDecoder.
                decoder = new WebCodecsDecoder(
                    config,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                        requestKeyframeOnDecoderError();
                    }
                );
                decoder.initialize();
                decoderConfigured = true;
                initialDescriptionApplied = true;

                // Sync lastRawDescription so decodeRawChunk doesn't redundantly reconfigure
                const desc = config.description;
                if (desc instanceof ArrayBuffer) {
                    lastRawDescription = desc.slice(0);
                } else if (ArrayBuffer.isView(desc)) {
                    lastRawDescription = desc.buffer.slice(
                        desc.byteOffset, desc.byteOffset + desc.byteLength) as ArrayBuffer;
                }
            } else {
                decoder = createDecoder(config);
                decoder.initialize();
                decoderConfigured = false;
                lastRawDescription = null;
                initialDescriptionApplied = false;
            }

            infoLog?.log('Decoder configured');
        } catch (error) {
            errorLog?.log('Error configuring decoder:', error);
            throw error;
        }
    },

    /**
   * Flush pending chunks
   */
    flush: async (): Promise<void> => {
        if (decoder) {
            try {
                await decoder.flush();
                infoLog?.log('Decoder flushed');
            } catch (error) {
                warnLog?.log('Decoder flush error:', error);
            }
        }
    },

    /**
   * Get current decoder statistics
   */
    // eslint-disable-next-line
    getStats: async (): Promise<DecoderStats> => {
        return getDecoderStatsSnapshot();
    },

    /**
   * Toggle between WASM and built-in decoders
   */
    // eslint-disable-next-line
    toggleDecoderType: async (useWasm: boolean): Promise<void> => {
        try {
            infoLog?.log('Toggling decoder type to', useWasm ? 'WASM' : 'built-in');

            if (!decoder) {
                throw new Error('Decoder not initialized');
            }

            infoLog?.log('WebCodecs decoder - using WebCodecs API');
        } catch (error) {
            errorLog?.log('Failed to toggle decoder type:', error);
            throw error;
        }
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    prewarmRpc: async (apiUrl: string): Promise<void> => {
        const wasInitialized = apiInitialized;
        ensureApiInitialized(apiUrl);
        if (!wasInitialized) {
            infoLog?.log('prewarmRpc: Api initialized, WS handshake started in parallel with decoder setup');
        }
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    startPullInWorker: async (
        streamId: string,
        skipToMs: number,
        apiUrl: string,
        startedAtMs: number,
        jitterBufferMs: number,
        writable?: WritableStream<VideoFrame>,
        bgCanvas?: OffscreenCanvas,
    ): Promise<void> => {
        if (mstgSelector) {
            warnLog?.log('startPullInWorker called while another selector is active — replacing');
            mstgSelector.dispose();
            mstgSelector = null;
        }

        let selectorWritable: WritableStream<VideoFrame>;
        if (writable) {
            // Tier 2: main constructed MSTG and already attached the track.
            selectorWritable = writable;
            infoLog?.log(`Off-thread renderer using main-supplied writable (tier 2), startedAtMs=${startedAtMs}, jitterBufferMs=${jitterBufferMs}`);
        } else {
            // Tier 1: try to construct generator inside this worker.
            const gen = tryCreateOffThreadGenerator();
            if (!gen) {
                throw new Error('Off-thread renderer unsupported: neither MediaStreamTrackGenerator nor VideoTrackGenerator is available in worker context');
            }
            selectorWritable = gen.writable;
            void callbacks.onOffThreadTrackReady(gen.track, rpcNoWait);
            infoLog?.log(`Off-thread renderer enabled in worker (tier 1, ${gen.api}), startedAtMs=${startedAtMs}, jitterBufferMs=${jitterBufferMs}`);
        }

        // Optional bg painter: low-res blurred canvas drawn from the same
        // VideoFrames the selector picks. Blur is applied via a WebGPU
        // dual-Kawase pipeline (see BgBlurRenderer) directly into the bg
        // canvas — no CPU readback. The renderer self-initializes on first
        // render() call; if WebGPU is unavailable in this worker context
        // the renderer fails closed and the backdrop stays blank.
        let bgPainter: { renderer: BgBlurRenderer } | undefined;
        if (bgCanvas) {
            bgPainter = { renderer: new BgBlurRenderer(bgCanvas) };
            infoLog?.log(`Bg painter armed: WebGPU Kawase blur every ${BG_DRAW_INTERVAL_MS}ms`);
        }

        mstgSelector = new WorkerMstgSelector(
            selectorWritable, startedAtMs, jitterBufferMs, bgPainter);

        // Idempotent — usually a no-op here because main calls prewarmRpc()
        // immediately after initialize(), starting the WS handshake in
        // parallel with decoder setup. This call is the safety net.
        ensureApiInitialized(apiUrl);

        pullStartedAtMs = startedAtMs;
        pullActive = true;
        activePullStreamId = streamId;
        resetPullStats();
        // Fire and forget — pull loop runs in background, ends via onPullEnded.
        void runPullLoop(streamId, skipToMs);
    },

    stopPullInWorker: async (): Promise<void> => {
        pullActive = false;
        activePullStreamId = null;
        if (pullAbortController) {
            pullAbortController.abort();
            pullAbortController = null;
        }
        await Promise.resolve();
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    setBgPaintEnabled: async (enabled: boolean): Promise<void> => {
        if (mstgSelector) mstgSelector.setBgPaintEnabled(enabled);
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    onConnectivityUpdate: async (
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> => {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
    }
};

// Two slightly different APIs produce equivalent (writable, MediaStreamTrack):
//   - MediaStreamTrackGenerator: Chromium (also exposed in workers). The
//     generator IS the MediaStreamTrack.
//   - VideoTrackGenerator: Safari worker-only. Has .track + .writable.
interface OffThreadGenerator {
    readonly track: MediaStreamTrack;
    readonly writable: WritableStream<VideoFrame>;
    readonly api: 'MediaStreamTrackGenerator' | 'VideoTrackGenerator';
}

function tryCreateOffThreadGenerator(): OffThreadGenerator | null {
    const g = globalThis as unknown as {
        MediaStreamTrackGenerator?: new (init: { kind: 'video' }) => MediaStreamTrack & { readonly writable: WritableStream<VideoFrame> };
        VideoTrackGenerator?: new () => { readonly track: MediaStreamTrack; readonly writable: WritableStream<VideoFrame> };
    };
    if (typeof g.MediaStreamTrackGenerator === 'function') {
        const generator = new g.MediaStreamTrackGenerator({ kind: 'video' });
        return { track: generator, writable: generator.writable, api: 'MediaStreamTrackGenerator' };
    }
    if (typeof g.VideoTrackGenerator === 'function') {
        const vtg = new g.VideoTrackGenerator();
        return { track: vtg.track, writable: vtg.writable, api: 'VideoTrackGenerator' };
    }
    return null;
}

// Initialize RPC communication (bidirectional)
const callbacks = rpcClientServer<DecoderWorkerCallbacks>(
    'DecoderWorker',
  self as unknown as Worker,
  serverImpl
);

infoLog?.log('Decoder worker initialized');
