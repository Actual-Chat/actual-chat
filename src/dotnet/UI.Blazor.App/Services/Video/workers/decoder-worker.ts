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
import type { EncodedChunkData } from '../webcodecs-encoder';
import { extractHVCC } from '../hevc-parser';
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
import { initAppConstants } from 'app-constants';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

// Worker state
let decoder: WebCodecsDecoder | null = null;
let processing = false;
let decoderConfigured = false;
let pendingChunks: EncodedChunkData[] = [];
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

// Extract an owned ArrayBuffer from a Uint8Array. msgpack-decoded byte fields
// may be either fully-owned (whole buffer = view) or shared subarrays into a
// larger decode buffer. Fast path: when the view spans the whole underlying
// buffer, return it directly — zero alloc, zero copy. Otherwise slice() to get
// an owned copy. The returned buffer is safe to detach (transfer or pass into
// `new EncodedVideoChunk({ transfer: [...] })`).
//
// Diagnostic counters: fast/slow path counts logged every
// OWNED_ARRAY_BUFFER_LOG_INTERVAL invocations to confirm msgpack ownership
// behaviour in production. If `slow` dominates, the receiver pays an extra
// ~16 KB alloc+copy per frame and a buffer-pool / msgpack tweak is worth it.
let ownedArrayBufferFastCount = 0;
let ownedArrayBufferSlowCount = 0;
const OWNED_ARRAY_BUFFER_LOG_INTERVAL = 300;
function ownedArrayBuffer(view: Uint8Array): ArrayBuffer {
    const isOwned = view.byteOffset === 0 && view.byteLength === view.buffer.byteLength;
    if (isOwned) {
        ownedArrayBufferFastCount++;
    } else {
        ownedArrayBufferSlowCount++;
    }
    const total = ownedArrayBufferFastCount + ownedArrayBufferSlowCount;
    if (total % OWNED_ARRAY_BUFFER_LOG_INTERVAL === 0) {
        const fastPct = (ownedArrayBufferFastCount / total * 100).toFixed(1);
        warnLog?.log(`ownedArrayBuffer: fast=${ownedArrayBufferFastCount} ` +
            `slow=${ownedArrayBufferSlowCount} (${fastPct}% fast)`);
    }
    if (isOwned) {
        // msgpack-decoded byte fields are always plain ArrayBuffer (never
        // SharedArrayBuffer); cast narrows the ArrayBufferLike union.
        return view.buffer as ArrayBuffer;
    }
    return view.slice().buffer;
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
    streamingApi.streamServer.RequestKeyFrame(sid)
        .catch((e: unknown) => warnLog?.log('RequestKeyFrame error:', e));
}

// Stream-based input reader loop promise (for cleanup)
let streamReadLoopPromise: Promise<void> | null = null;

// Off-thread MSTG render path: when set, decoded frames are routed into the
// selector instead of being emitted to main via onDecodedFrame.
let mstgSelector: WorkerMstgSelector | null = null;

// In-worker Fusion RPC pull state (§9). When pullActive, the worker iterates
// `streamingApi.streamServer.GetVideo(...)` itself and feeds chunks into the
// decoder — main never sees per-frame work on this path.
let pullActive = false;
let pullAbortController: AbortController | null = null;
let pullStartedAtMs = 0;
let pullRetryCount = 0;
let pullSequenceNumber = 0;
const PULL_LATENCY_REPORT_INTERVAL_MS = 2000;
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

function resetPullStats(): void {
    pullReceivedBytes = 0;
    pullReceivedFrameCount = 0;
    pullReceivedKeyframeCount = 0;
    pullFirstFrameAt = 0;
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
    }
    return base;
}

function buildLatencyReport(streamOffsetMs: number): DecoderWorkerLatencyReport {
    const ds = getDecoderStatsSnapshot();
    const selectorStats = mstgSelector?.getBufferStats();
    return {
        streamOffsetMs,
        medianDecodeTimeMs: ds.pureMedianDecodeTime >= 0 ? ds.pureMedianDecodeTime : ds.medianDecodeTime,
        bufferDepth: (selectorStats?.depth ?? 0) + ds.decodeQueueSize,
        bufferSpanMs: selectorStats?.spanMs ?? -1,
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

// Chunk ordering state to prevent out-of-order decoding issues
let nextExpectedSequence = 0;
const reorderBuffer = new Map<number, EncodedChunkData>();
let lastKeyframeSequence = -1;
const MAX_REORDER_GAP = 5; // If we receive packets 5+ ahead, assume intermediate ones are lost
let waitingForKeyframe = false; // Flag to indicate we're in error recovery mode

// Process buffered chunks in sequence order
function processBufferedChunks(): void {
    while (reorderBuffer.has(nextExpectedSequence)) {
        const chunk = reorderBuffer.get(nextExpectedSequence)!;
        reorderBuffer.delete(nextExpectedSequence);
        decodeChunk(chunk);
        nextExpectedSequence++;
    }
}

// Extract codec family prefix for comparison (e.g., 'avc1' from 'avc1.640028', 'av01' from 'av01.0.08M.08')
function codecFamily(codec: string): string {
    return codec.substring(0, 4);
}

// Handle codec change: flush+close old decoder, create new one with updated config
function handleCodecChange(chunkData: EncodedChunkData): void {
    const newCodec = chunkData.codec!;
    const oldCodec = currentDecoderConfig!.codec;
    infoLog?.log(`Codec change detected: ${oldCodec} -> ${newCodec}, reconfiguring decoder`);

    // 1. Flush + close old decoder
    if (decoder) {
        try {
            if (decoder.getState() === 'configured') {
                // Can't await in sync context, just close
                decoder.close();
            }
        } catch (error) {
            warnLog?.log('Error closing old decoder during codec switch:', error);
        }
    }

    // 2. Update config with new codec
    currentDecoderConfig = { ...currentDecoderConfig!, codec: newCodec, description: undefined };

    // 3. Create new decoder
    decoder = new WebCodecsDecoder(
        { ...currentDecoderConfig, description: undefined },
        emitDecodedFrame,
        (error) => {
            errorLog?.log('Decoder error:', error);
            requestKeyframeOnDecoderError();
        }
    );
    decoder.initialize();

    // 4. Reset state
    decoderConfigured = false;
    pendingChunks = [];
    reorderBuffer.clear();
    lastKeyframeSequence = -1;
    waitingForKeyframe = false;
    nextExpectedSequence = chunkData.sequenceNumber;

    infoLog?.log(`Decoder reconfigured for codec ${newCodec}, resuming at sequence #${chunkData.sequenceNumber}`);
}

// Decode a single chunk (guaranteed to be in sequence order)
function decodeChunk(chunkData: EncodedChunkData): void {
    const seq = chunkData.sequenceNumber;

    try {
    // Auto-detect codec change from keyframe data
        if (chunkData.type === 'key' && chunkData.codec && currentDecoderConfig) {
            const incomingFamily = codecFamily(chunkData.codec);
            const currentFamily = codecFamily(currentDecoderConfig.codec);
            if (incomingFamily !== currentFamily) {
                handleCodecChange(chunkData);
                // Fall through to normal keyframe processing below
            }
        }

        // Track keyframes for decoder recovery
        if (chunkData.type === 'key') {
            lastKeyframeSequence = seq;
        }

        // If decoder is closed and this is a keyframe, attempt recovery
        if (decoder && decoder.getState() === 'closed' && chunkData.type === 'key') {
            // HEVC/AVC require description on every configure() — bake it into the
            // recovery config so the next keyframe doesn't fail with
            // "A key frame is required after configure()" DataError.
            const metadataDesc = chunkData.metadata?.decoderConfig?.description;
            const recoveryDescription = metadataDesc
                ?? lastRawDescription
                ?? currentDecoderConfig?.description;
            infoLog?.log(`Decoder closed, attempting recovery with keyframe #${seq} (descLen=${
                recoveryDescription ? (recoveryDescription as ArrayBuffer).byteLength : 0})`);

            try {
                const recoveryConfig: DecoderConfig = recoveryDescription
                    ? { ...currentDecoderConfig!, description: recoveryDescription }
                    : { ...currentDecoderConfig!, description: undefined };
                decoder = new WebCodecsDecoder(
                    recoveryConfig,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                        requestKeyframeOnDecoderError();
                    }
                );

                decoder.initialize();
                infoLog?.log(`Decoder recovered at keyframe #${seq}`);
                decoderConfigured = !!recoveryDescription;
                if (recoveryDescription)
                    lastRawDescription = (recoveryDescription as ArrayBuffer).slice(0);
            } catch (error) {
                errorLog?.log('Failed to recover decoder:', error);
                return;
            }
        }

        // If decoder is still closed (not a keyframe or recovery failed), skip this chunk
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Handle first keyframe with metadata
        if (!decoderConfigured && chunkData.type === 'key') {
            infoLog?.log(`First keyframe #${seq} received`);

            let description: AllowSharedBufferSource | undefined;

            // Try to get description from encoder metadata first
            if (chunkData.metadata?.decoderConfig?.description) {
                infoLog?.log('Using description from encoder metadata');
                description = chunkData.metadata.decoderConfig.description;
            }
            // For HEVC, try manual HVCC extraction as fallback
            else if (currentDecoderConfig?.codec.startsWith('hev1') || currentDecoderConfig?.codec.startsWith('hvc1')) {
                infoLog?.log('Attempting manual HVCC extraction for HEVC');
                const hvcc = extractHVCC(chunkData.chunk);
                if (hvcc) {
                    infoLog?.log('Successfully extracted HVCC from bitstream');
                    description = hvcc;
                } else {
                    warnLog?.log('Failed to extract HVCC, decoder may fail');
                }
            } else {
                infoLog?.log('No metadata description - decoder will auto-configure');
            }

            // Reconfigure decoder with description if available
            if (description && decoder) {
                infoLog?.log('Reconfiguring decoder with description');
                decoder.updateDescription(description);
            }

            // Mark as configured so we start decoding
            decoderConfigured = true;

            // Decode the keyframe
            if (decoder) {
                decoder.decode(chunkData);
                infoLog?.log(`First keyframe #${seq} decoded successfully`);
            }

            // Process any buffered chunks from before configuration
            if (pendingChunks.length > 0) {
                infoLog?.log('Processing', pendingChunks.length, 'buffered chunks');
                for (const bufferedChunk of pendingChunks) {
                    if (decoder) {
                        decoder.decode(bufferedChunk);
                    }
                }
                pendingChunks = [];
            }

            return;
        }

        // Check decoder state before attempting to decode
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Decode chunks directly
        if (decoderConfigured && decoder) {
            decoder.decode(chunkData);
        } else {
            // Buffer until decoder is configured with first keyframe
            debugLog?.log('Buffering chunk until configured');
            pendingChunks.push(chunkData);
        }
    } catch (error) {
        errorLog?.log(`Error decoding chunk #${seq}:`, error);

        // If we have a recent keyframe, try to recover
        if (lastKeyframeSequence >= 0 && reorderBuffer.has(lastKeyframeSequence)) {
            infoLog?.log(`Attempting recovery from buffered keyframe #${lastKeyframeSequence}`);
        }
    }
}

/**
 * Emit a decoded frame to the appropriate output (stream or RPC callback).
 */
function emitDecodedFrame(frame: VideoFrame): void {
    frameCount++;
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

// In-worker pull loop. Iterates `streamingApi.streamServer.GetVideo(...)`,
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
        infoLog?.log(`pull: GetVideo(${streamId}, skipTo=${skipToMs}ms)`);
        const stream = await streamingApi.streamServer.GetVideo(streamId, skipToTicks);

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
            const dataBuffer = ownedArrayBuffer(data);
            let descBuffer: ArrayBuffer | undefined;
            const desc = frame.Description;
            if (desc && desc.length > 0) {
                descBuffer = ownedArrayBuffer(desc);
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
            if (now - lastLatencyReportAt > PULL_LATENCY_REPORT_INTERVAL_MS) {
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
                const retrySkipToMs = Math.max(0, Date.now() - pullStartedAtMs);
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
            const retrySkipToMs = Math.max(0, Date.now() - pullStartedAtMs);
            void runPullLoop(streamId, retrySkipToMs);
        }, delay);
    }
}

// RPC Server Implementation
const serverImpl: DecoderWorker = {
    init: (appConstants): Promise<void> => {
        initAppConstants(appConstants);
        return Promise.resolve();
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
            warnLog?.log(`[INIT_DEFERRED] Decoder created, configure() deferred to first keyframe, ` +
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
            warnLog?.log(`[INIT_DEFERRED] Decoder created (stream), configure() deferred to first keyframe, ` +
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
            pendingChunks = [];
            currentDecoderConfig = null;
            frameCount = 0;
            lastRawDescription = null;
            nextExpectedSequence = 0;
            reorderBuffer.clear();
            lastKeyframeSequence = -1;
            firstKeyframeLogged = false;
            lastChunkSeq = -1;
            lastChunkType = '?';
            lastChunkSize = 0;
            lastChunkDescLen = 0;
            waitingForKeyframe = false;
            initialDescriptionApplied = false;
        } catch (error) {
            errorLog?.log('Failed to stop decoder:', error);
            throw error;
        }
    },

    /**
   * Decode an encoded chunk (legacy path — EncodedChunkData with EncodedVideoChunk)
   */
    // eslint-disable-next-line
    decodeChunk: async (chunkData): Promise<void> => {
        if (!processing) {
            warnLog?.log('Dropping chunk - not processing');
            return;
        }

        const seq = chunkData.sequenceNumber;

        // If we're waiting for a keyframe due to packet loss, drop all non-keyframe chunks
        if (waitingForKeyframe && chunkData.type !== 'key') {
            return;
        }

        // If this is a keyframe and we were waiting for one, reset recovery mode
        if (waitingForKeyframe && chunkData.type === 'key') {
            infoLog?.log(`Recovery keyframe #${seq} received`);
            waitingForKeyframe = false;
            reorderBuffer.clear();
            nextExpectedSequence = seq;
            decodeChunk(chunkData);
            nextExpectedSequence = seq + 1;
            return;
        }

        // Handle out-of-order delivery: buffer chunks until we can process in sequence
        if (seq !== -1 && seq !== nextExpectedSequence) {
            const gap = seq - nextExpectedSequence;
            debugLog?.log(`Out-of-order chunk #${seq} (expecting #${nextExpectedSequence}), gap:`, gap);
            reorderBuffer.set(seq, chunkData);

            if (gap >= MAX_REORDER_GAP) {
                warnLog?.log(`Gap of ${gap} detected, packet #${nextExpectedSequence} is likely lost`);

                let hasKeyframeInBuffer = false;
                let firstKeyframeSeq = -1;
                for (const [bufSeq, bufChunk] of reorderBuffer) {
                    if (bufChunk.type === 'key' && bufSeq > nextExpectedSequence) {
                        hasKeyframeInBuffer = true;
                        firstKeyframeSeq = firstKeyframeSeq === -1 ? bufSeq : Math.min(firstKeyframeSeq, bufSeq);
                    }
                }

                if (hasKeyframeInBuffer) {
                    infoLog?.log(`Found keyframe #${firstKeyframeSeq} in buffer, skipping to it`);
                    for (const [bufSeq] of reorderBuffer) {
                        if (bufSeq < firstKeyframeSeq) {
                            reorderBuffer.delete(bufSeq);
                        }
                    }
                    nextExpectedSequence = firstKeyframeSeq;
                    processBufferedChunks();
                } else {
                    warnLog?.log(`No keyframe in buffer after lost packet #${nextExpectedSequence}, entering recovery mode`);
                    waitingForKeyframe = true;
                    reorderBuffer.clear();
                }
                return;
            }

            if (chunkData.type === 'key') {
                debugLog?.log(`Received keyframe #${seq} while waiting for #${nextExpectedSequence}`);
                nextExpectedSequence = seq;
                decodeChunk(chunkData);
                nextExpectedSequence = seq + 1;
                for (const [bufSeq] of reorderBuffer) {
                    if (bufSeq < seq) {
                        reorderBuffer.delete(bufSeq);
                    }
                }
                processBufferedChunks();
                return;
            }

            processBufferedChunks();
            return;
        }

        // Process this chunk immediately (it's in order)
        decodeChunk(chunkData);

        if (seq !== -1) {
            nextExpectedSequence = seq + 1;
            processBufferedChunks();
        }
    },

    /**
     * Decode raw encoded bytes (used by video-player.ts for off-main-thread decoding).
     * Creates EncodedVideoChunk internally from raw bytes.
     */
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
            warnLog?.log(
                `[FIRST_KF_DIAG] seq=${sequenceNumber}, dataLen=${dataLen}, ` +
                `initDescLen=${initLen}, chunkDescLen=${chunkLen}, cmp=${initVsChunk}, ` +
                `decoderState=${decoder.getState()}, decoderConfigured=${decoderConfigured}, ` +
                `codec=${currentDecoderConfig?.codec}, hwAccel=${currentDecoderConfig?.hardwareAcceleration}`);
            warnLog?.log(`[FIRST_KF_DIAG] initDescHex=${initHex}`);
            warnLog?.log(`[FIRST_KF_DIAG] chunkDescHex=${chunkHex}`);
            warnLog?.log(`[FIRST_KF_DIAG] dataHex=${dataHex}`);
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
                    warnLog?.log(`[FIRST_KF_FRESH] built fresh decoder for first keyframe, ` +
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
                        decoder.updateDescription(description, selection.codec);
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
                warnLog?.log(`[PRE_DECODE] Decoding keyframe: seq=${sequenceNumber}, ` +
                    `state=${decoder.getState()}, configured=${decoderConfigured}, ` +
                    `descLen=${description?.byteLength ?? 0}, dataLen=${dataLen}, ` +
                    `flagWasUsed=${!initialDescriptionApplied && sequenceNumber === 0 ? 'maybe' : 'n/a'}`);
            }

            // Decode using the WebCodecsDecoder wrapper (tracks timing for diagnostics)
            decoder.decodeRaw(chunk);
        } catch (error) {
            errorLog?.log('Error decoding raw chunk:', error);
        }
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
        serverClockOffsetMs: number,
        jitterBufferMs: number,
        syncPort: MessagePort,
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
                try { syncPort.close(); } catch { /* ignore */ }
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
            selectorWritable, syncPort, startedAtMs, serverClockOffsetMs, jitterBufferMs, bgPainter);

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
    getDriftMs: async (): Promise<number> => {
        return mstgSelector ? mstgSelector.getDriftMs() : 0;
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
