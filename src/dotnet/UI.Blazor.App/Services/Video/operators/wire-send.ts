import { from, type PipeOperator } from 'ix-ext';
import { abortPromise } from 'promises';
import { getLogs } from 'logging';
import {
    disposeEncodedBundle,
    type EncodedBundle,
    type EncodedFrame,
    type VideoRecordingStats,
} from '../frame-envelopes';

const { warnLog } = getLogs('VideoPipeline');

// Wire DTO consumed by the .NET sender (MessagePack `VideoFrame`).
// offset/duration are 100-ns ticks; description/codec/sourceWidth/Height are keyframe-only.
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
    layerId?: number;
    maxLayerId?: number;
    sourceWidth?: number;
    sourceHeight?: number;
}

// One source-moment's worth of per-layer wire frames, sent as a single RpcStream item.
export interface VideoStreamFrameBundle {
    layers: VideoStreamFrame[];
}

export interface StreamFormat {
    codec: string;
    width: number;
    height: number;
    sourceWidth: number;
    sourceHeight: number;
    // Free-form codec hint (e.g. base64 HEVC description); empty when no extra negotiation is needed.
    codecSettings: string;
}

export interface StreamSenderLike {
    send(dto: VideoStreamFrameBundle): void | Promise<void>;
    init?(format: StreamFormat): void;
    // wireSend short-circuits if this resolves while source is still producing —
    // silently piling chunks into a dead queue would mask peer-change / server-close.
    readonly whenDisposed?: Promise<void>;
    dispose?(): void;
    getStats?(): StreamSenderStats;
}

export interface StreamSenderStats {
    addedFrameCount: number;
    queueDepth: number;
    maxQueueDepth: number;
    // Frames RpcStreamSender skipped inside its local ring via canSkipTo=isKeyFrame.
    // Real-time compaction — NOT a queue-level loss counter (that's floodGateSkipCount).
    rpcStreamSkipped: number;
    floodGateSkipCount: number;
    lastAckAgeMs: number;
    isPeerConnected: boolean;
}

export interface WireSendOptions {
    createSender: () => StreamSenderLike;
    // Fills MaxLayerId on every chunk; without it, consumers clamp to L0.
    layerCount?: number;
    // Encoder yields bottom-first; we wait for the top-layer keyframe before init.
    topLayerWidth?: number;
    topLayerHeight?: number;
    // Aborts mid-send so Recorder.stop() doesn't block on a stalled ring buffer / dead peer.
    abortSignal?: AbortSignal;
}

const TICKS_PER_MICROSECOND = 10;

function microsecondsToTicks(microseconds: number): number {
    return microseconds * TICKS_PER_MICROSECOND;
}

// captureStartUnixMs anchors stream-relative offset and never resets across the
// run; epoch flips ride in offsetEpoch so the receiver rebases pacing without
// rebasing offset.
export function wireSend(opts: WireSendOptions): PipeOperator<EncodedBundle, void> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<void> {
            const { createSender, topLayerWidth, topLayerHeight, abortSignal } = opts;
            const layerCount = opts.layerCount ?? 1;
            const maxLayerId = layerCount - 1;
            const abortRace: Promise<never> = abortSignal
                ? abortPromise(abortSignal)
                : new Promise(() => { /* never resolves */ });
            let sender: StreamSenderLike | null = null;
            let captureStartUnixMs = Number.NaN;
            let initSent = false;
            let pumpFailure: Error | null = null;
            let pumpResolved = false;
            let lastStats: EncodedBundle['stats'] | null = null;
            const getPumpFailure = (): Error | null => pumpFailure;
            const resetSender = (reason: string): void => {
                if (sender && lastStats)
                    copySenderStats(lastStats, sender.getStats?.());
                try { sender?.dispose?.(); } catch { /* ignore */ }
                sender = null;
                initSent = false;
                pumpFailure = null;
                pumpResolved = false;
                warnLog?.log(`wireSend: resetting wire sender — ${reason}`);
            };
            // HEVC: later keyframes may omit description; cache the first one
            // so the receiver can always reconfigure on dim change.
            const descriptionByLayer = new Map<number, Uint8Array>();
            try {
                for await (const bundle of source) {
                    if (abortSignal?.aborted) return;

                    try {
                        if (bundle.layers.length === 0)
                            continue;
                        lastStats = bundle.stats;
                        const top = bundle.layers[bundle.layers.length - 1];
                        const { capturedAt } = top;
                        const isKeyFrame = top.chunk.type === 'key';
                        if (Number.isNaN(captureStartUnixMs))
                            captureStartUnixMs = capturedAt.timeMs;

                        const offsetMicros = Math.round((capturedAt.timeMs - captureStartUnixMs) * 1000);
                        const offset = microsecondsToTicks(offsetMicros);

                        const wireLayers: VideoStreamFrame[] = bundle.layers.map(encoded => {
                            const dto: VideoStreamFrame = {
                                offset,
                                offsetEpoch: capturedAt.epoch,
                                duration: microsecondsToTicks(encoded.chunk.duration ?? 0),
                                isKeyFrame: encoded.chunk.type === 'key',
                                width: encoded.encodedWidth,
                                height: encoded.encodedHeight,
                                data: readChunkBytes(encoded.chunk),
                                layerId: encoded.layerId,
                                maxLayerId,
                                temporalLayerId: encoded.metadata.temporalLayerId,
                            };
                            if (dto.isKeyFrame) {
                                dto.sourceWidth = encoded.sourceWidth;
                                dto.sourceHeight = encoded.sourceHeight;
                                const description = resolveDescription(encoded, descriptionByLayer);
                                if (description) dto.description = description;
                            }
                            return dto;
                        });

                        const failure = getPumpFailure();
                        if (failure)
                            resetSender(failure.message);
                        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                        if (pumpResolved)
                            resetSender('wire pump completed before source drained');

                        if (!sender) {
                            sender = createSender();
                            sender.whenDisposed?.then(
                                () => { pumpResolved = true; },
                                (e: unknown) => {
                                    pumpFailure = e instanceof Error ? e : new Error(String(e));
                                },
                            );
                        }
                        if (!initSent && isKeyFrame && sender.init) {
                            const description = resolveDescription(top, descriptionByLayer);
                            sender.init({
                                codec: top.metadata.decoderConfig?.codec ?? '',
                                width: topLayerWidth ?? top.encodedWidth,
                                height: topLayerHeight ?? top.encodedHeight,
                                sourceWidth: top.sourceWidth,
                                sourceHeight: top.sourceHeight,
                                codecSettings: description ? bytesToBase64(description) : '',
                            });
                            initSent = true;
                        }
                        // Race against abort: send can hang on a full ring buffer
                        // when the consumer iterator stalls, blocking Recorder.stop().
                        await Promise.race([
                            Promise.resolve(sender.send({ layers: wireLayers })),
                            abortRace,
                        ]);
                        copySenderStats(bundle.stats, sender.getStats?.());
                    } finally {
                        disposeEncodedBundle(bundle);
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
    stats: VideoRecordingStats,
    senderStats: StreamSenderStats | undefined,
): void {
    if (!senderStats)
        return;

    stats.wireFramesAdded = senderStats.addedFrameCount;
    stats.wireQueueDepth = senderStats.queueDepth;
    stats.wireMaxQueueDepth = senderStats.maxQueueDepth;
    stats.rpcStreamFramesSkipped = senderStats.rpcStreamSkipped;
    stats.floodGateSkipCount = senderStats.floodGateSkipCount;
    stats.wireLastAckAgeMs = senderStats.lastAckAgeMs;
    stats.isPeerConnected = senderStats.isPeerConnected;
}

// Copy on insert so later mutation of metadata.decoderConfig.description
// can't bleed into earlier wire frames.
function resolveDescription(
    encoded: EncodedFrame,
    cache: Map<number, Uint8Array>,
): Uint8Array | null {
    const raw = encoded.metadata.decoderConfig?.description as ArrayBuffer | ArrayBufferView | undefined;
    if (raw) {
        const bytes = toUint8Array(raw);
        cache.set(encoded.layerId, bytes);
        return bytes;
    }
    return cache.get(encoded.layerId) ?? null;
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

// Chunked Latin-1 base64: workers don't expose Buffer, and HVCC/SPS inputs
// can overflow String.fromCharCode(...) in one shot.
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
