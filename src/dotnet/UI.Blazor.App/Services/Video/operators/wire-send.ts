import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'promises';
import { closeEncodedChunk, type EncodedFrame } from '../frame-envelopes';

// Wire DTO consumed by the .NET sender (MessagePack `VideoFrame`).
// `offset`/`duration` are in 100-ns ticks (= µs × 10). `description`,
// `codec`, `sourceWidth/Height` are keyframe-only.
export interface VideoStreamFrame {
    offset: number;
    offsetEpoch?: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array;
    codec?: string;
    temporalLayerId?: number;
    spatialLayerId?: number;
    minSpatialLayerId?: number;
    maxSpatialLayerId?: number;
    sourceWidth?: number;
    sourceHeight?: number;
}

// Stream-format payload sent via `StreamSenderLike.init` from the first
// top-layer keyframe (encoder output is bottom-first, so we wait for it).
export interface StreamFormat {
    codec: string;
    width: number;
    height: number;
    sourceWidth: number;
    sourceHeight: number;
    /** Free-form codec hint (e.g. base64 HEVC description). Empty when
     *  no extra negotiation is needed. */
    codecSettings: string;
}

/** Production binding: `RpcStreamSender<VideoFrameDto>` via
 *  `InternalVideoStream`. Tests pass an in-memory recorder. */
export interface StreamSenderLike {
    send(dto: VideoStreamFrame): void | Promise<void>;
    init?(format: StreamFormat): void;
    /** When set, `wireSend` short-circuits if the underlying pump
     *  resolves while the source is still producing — silently piling
     *  chunks into a dead queue would mask peer-change / server-close. */
    readonly whenDisposed?: Promise<void>;
    /** Called from the operator's `finally` so the server-side iterator
     *  returns and `PushStream` completes deterministically. */
    dispose?(): void;
    getStats?(): StreamSenderStats;
}

export interface StreamSenderStats {
    addedFrameCount: number;
    queueDepth: number;
    maxQueueDepth: number;
    droppedAtSenderQueue: number;
    droppedKeyframesAtSenderQueue: number;
    rpcStreamSkipped: number;
    lastAckAgeMs: number;
    isPeerConnected: boolean;
}

export interface WireSendOptions {
    createSender: () => StreamSenderLike;
    /** Number of active spatial layers — fills `[Min,Max]SpatialLayerId`
     *  on every chunk so the server's `ReceiveQualityFilter` knows the
     *  producer's range. Without it, consumers clamp to L0. */
    layerCount?: number;
    /** Top-layer encoded dims for the `init` payload. Encoder yields
     *  bottom-first; we wait for the top-layer keyframe before init. */
    topLayerWidth?: number;
    topLayerHeight?: number;
    /** Aborts mid-`send` so a `Recorder.stop()` doesn't block on a
     *  stalled `RpcStreamSender` ring buffer / dead peer. */
    abortSignal?: AbortSignal;
}

const TICKS_PER_MICROSECOND = 10;

function microsecondsToTicks(microseconds: number): number {
    return microseconds * TICKS_PER_MICROSECOND;
}

/**
 * Terminal sink: `EncodedFrame → VideoStreamFrame` via `StreamSenderLike`.
 *
 * `captureStartUnixMs` (NaN sentinel until the first chunk) anchors the
 * stream-relative offset and never resets across the run — epoch flips
 * are carried in `offsetEpoch`, the receiver uses that to rebase pacing
 * without rebasing offset.
 */
export function wireSend(opts: WireSendOptions): PipeOperator<EncodedFrame, void> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<void> {
            const { createSender, topLayerWidth, topLayerHeight, abortSignal } = opts;
            const layerCount = opts.layerCount ?? 1;
            const maxSpatialLayerId = layerCount - 1;
            const abortRace: Promise<never> = abortSignal
                ? abortPromise(abortSignal)
                : new Promise(() => { /* never resolves */ });
            let sender: StreamSenderLike | null = null;
            let captureStartUnixMs = Number.NaN;
            let initSent = false;
            // Pump terminal state — see StreamSenderLike.whenDisposed comment.
            let pumpFailure: Error | null = null;
            let pumpResolved = false;
            let lastStats: EncodedFrame['stats'] | null = null;
            // HEVC: later keyframes per spec may omit the description; fill
            // from cache so the receiver can always reconfigure on dim change.
            const descriptionByLayer = new Map<number, Uint8Array>();
            try {
                for await (const encoded of source) {
                    if (abortSignal?.aborted) return;

                    try {
                        lastStats = encoded.stats;
                        const { capturedAt, spatialLayerId } = encoded;
                        const isKeyFrame = encoded.chunk.type === 'key';
                        if (Number.isNaN(captureStartUnixMs))
                            captureStartUnixMs = capturedAt.timeMs;

                        const offsetMicros = Math.round((capturedAt.timeMs - captureStartUnixMs) * 1000);
                        const offset = microsecondsToTicks(offsetMicros);
                        const data = readChunkBytes(encoded.chunk);
                        const dto: VideoStreamFrame = {
                            offset,
                            duration: microsecondsToTicks(encoded.chunk.duration ?? 0),
                            isKeyFrame,
                            width: encoded.encodedWidth,
                            height: encoded.encodedHeight,
                            data,
                            spatialLayerId,
                            minSpatialLayerId: 0,
                            maxSpatialLayerId,
                            temporalLayerId: encoded.metadata.temporalLayerId,
                        };
                        dto.offsetEpoch = capturedAt.epoch;
                        if (isKeyFrame) {
                            dto.sourceWidth = encoded.sourceWidth;
                            dto.sourceHeight = encoded.sourceHeight;
                            const description = resolveDescription(encoded, descriptionByLayer);
                            if (description) dto.description = description;
                        }
                        // Async-set flags (mutated by `whenDisposed` callback below).
                        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition, @typescript-eslint/only-throw-error
                        if (pumpFailure) throw pumpFailure;
                        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                        if (pumpResolved)
                            throw new Error('wireSend: wire pump completed before source drained');

                        if (!sender) {
                            sender = createSender();
                            sender.whenDisposed?.then(
                                () => { pumpResolved = true; },
                                (e: unknown) => {
                                    pumpFailure = e instanceof Error ? e : new Error(String(e));
                                },
                            );
                        }
                        if (!initSent && isKeyFrame && spatialLayerId === maxSpatialLayerId && sender.init) {
                            const description = resolveDescription(encoded, descriptionByLayer);
                            sender.init({
                                codec: encoded.metadata.decoderConfig?.codec ?? '',
                                width: topLayerWidth ?? encoded.encodedWidth,
                                height: topLayerHeight ?? encoded.encodedHeight,
                                sourceWidth: encoded.sourceWidth,
                                sourceHeight: encoded.sourceHeight,
                                codecSettings: description ? bytesToBase64(description) : '',
                            });
                            initSent = true;
                        }
                        // Race against abort — `RpcStreamSender.send` can
                        // hang on a full ring buffer when the consumer-side
                        // iterator stalls, blocking `Recorder.stop()`.
                        await Promise.race([
                            Promise.resolve(sender.send(dto)),
                            abortRace,
                        ]);
                        copySenderStats(encoded.stats, sender.getStats?.());
                    } finally {
                        closeEncodedChunk(encoded.chunk);
                    }
                }
            } finally {
                if (sender && lastStats)
                    copySenderStats(lastStats, sender.getStats?.());
                try { sender?.dispose?.(); } catch { /* ignore */ }
            }
        }
    };
}

function copySenderStats(
    stats: EncodedFrame['stats'],
    senderStats: StreamSenderStats | undefined,
): void {
    if (!senderStats)
        return;

    stats.wireFramesAdded = senderStats.addedFrameCount;
    stats.wireQueueDepth = senderStats.queueDepth;
    stats.wireMaxQueueDepth = senderStats.maxQueueDepth;
    stats.wireFramesDropped = senderStats.droppedAtSenderQueue;
    stats.wireKeyframesDropped = senderStats.droppedKeyframesAtSenderQueue;
    stats.rpcStreamFramesSkipped = senderStats.rpcStreamSkipped;
    stats.wireLastAckAgeMs = senderStats.lastAckAgeMs;
    stats.isPeerConnected = senderStats.isPeerConnected;
}

// Per-layer cache: copy on insert so later mutation of
// `metadata.decoderConfig.description` can't bleed into earlier wire frames.
function resolveDescription(
    encoded: EncodedFrame,
    cache: Map<number, Uint8Array>,
): Uint8Array | null {
    const raw = encoded.metadata.decoderConfig?.description as ArrayBuffer | ArrayBufferView | undefined;
    if (raw) {
        const bytes = toUint8Array(raw);
        cache.set(encoded.spatialLayerId, bytes);
        return bytes;
    }
    return cache.get(encoded.spatialLayerId) ?? null;
}

function toUint8Array(source: ArrayBuffer | ArrayBufferView): Uint8Array {
    if (source instanceof Uint8Array) return new Uint8Array(source);
    if (ArrayBuffer.isView(source)) {
        const view = new Uint8Array(source.buffer as ArrayBuffer, source.byteOffset, source.byteLength);
        return new Uint8Array(view);
    }
    return new Uint8Array(source.slice(0));
}

function readChunkBytes(chunk: EncodedVideoChunk): Uint8Array {
    const buffer = new ArrayBuffer(chunk.byteLength);
    chunk.copyTo(buffer);
    return new Uint8Array(buffer);
}

// Chunked Latin-1 base64 — workers don't expose `Buffer`, and inputs
// (HVCC / SPS) can be large enough to overflow `String.fromCharCode(...)`.
function bytesToBase64(bytes: Uint8Array): string {
    if (bytes.byteLength === 0) return '';
    const CHUNK = 0x8000;
    let s = '';
    for (let i = 0; i < bytes.byteLength; i += CHUNK) {
        const slice = bytes.subarray(i, Math.min(i + CHUNK, bytes.byteLength));
        s += String.fromCharCode(...slice);
    }
    return btoa(s);
}
