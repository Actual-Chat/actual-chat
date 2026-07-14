import { from, type PipeOperator } from 'ix-ext';
import { RunningEMA } from 'math';
import { abortPromise } from 'actuallab-core';
import { getLogs } from 'logging';
import {
    aggregateDropTrace,
    disposeEncodedBundle,
    type EncodedBundle,
    type RecorderStats,
} from '../frame-envelopes';
import type { LayerLadderController } from '../sender/layer-ladder-controller';

const WIRE_QUEUE_DEPTH_EMA_ALPHA = 0.2;
// Upper bound on waiting for the wire to flush $sys.End after dispose(). Must
// stay well under the C# stop budget (3s) so a dead wire can't hang stop().
const WIRE_END_FLUSH_TIMEOUT_MS = 1000;

const { warnLog } = getLogs('VideoPipeline');

// Wire DTO consumed by the .NET sender (MessagePack `VideoFrame`).
// offset/duration are 100-ns ticks; description/codec are keyframe-only.
// IsKeyFrame is NOT on the wire — derived as `keyFrameIndex === index`.
export interface VideoStreamFrame {
    offset: number;
    offsetEpoch?: number;
    duration: number;
    // Per-layer pointer to the keyframe this frame belongs to (the keyframe's own `index`).
    keyFrameIndex: number;
    // Sender-assigned source-moment counter.
    index: number;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array;
    codec?: string;
    layerId?: number;
    // Canonical ladder size (max canonical layer id + 1); ids are stable.
    layerCount?: number;
    // Bitmask of canonical layer ids currently encoded.
    layerMask?: number;
    // Quarter-turn CW (0|1|2|3) the receiver should apply to display upright.
    // Omitted when 0 — keeps the wire compact for the common case.
    rotation?: number;
    // FrameDropStage[] — one entry per dropped predecessor up to this point.
    dropTrace?: Uint8Array;
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
    // Windowed-min ack round trip (ms, -1 until sampled) ≈ propagation RTT.
    minRttMs: number;
    // RpcStream local ring occupancy (bundles) as of the last push into it.
    ringDepth: number;
    isPeerConnected: boolean;
    // Cumulative bytes drained out of the wire send buffer (handed to the
    // RpcStream as it pulls; the pull is ack-window-gated). Delta/time while the
    // buffer is backlogged ≈ the wire delivery rate = uplink capacity.
    ackedBytes: number;
}

export interface WireSendOptions {
    createSender: () => StreamSenderLike;
    // Drives per-bundle LayerCount stamping and the first-keyframe sender.init
    // dims; reads on every bundle so hot-applied changes propagate.
    controller: LayerLadderController;
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
            const { createSender, controller, abortSignal } = opts;
            const wireQueueDepthEma = new RunningEMA(0, 1, WIRE_QUEUE_DEPTH_EMA_ALPHA);
            const wireRingDepthEma = new RunningEMA(0, 1, WIRE_QUEUE_DEPTH_EMA_ALPHA);
            // Reconnect streak: persists across resetSender() calls so a burst
            // of disconnects accrues correctly.
            const reconnectTracker = { streak: 0, prevConnected: false };
            const copyStats = (stats: RecorderStats, sender: StreamSenderLike | null): void => {
                if (!sender)
                    return;

                const senderStats = sender.getStats?.();
                if (!senderStats)
                    return;

                stats.wireLastAckAgeMs = senderStats.lastAckAgeMs;
                stats.wireMinRttMs = senderStats.minRttMs;
                stats.isPeerConnected = senderStats.isPeerConnected;
                stats.wireAckedBytes = senderStats.ackedBytes;
                wireQueueDepthEma.appendSample(senderStats.queueDepth);
                stats.wireQueueDepthEma = wireQueueDepthEma.value;
                wireRingDepthEma.appendSample(senderStats.ringDepth);
                stats.wireRingDepthEma = wireRingDepthEma.value;
                if (!senderStats.isPeerConnected && reconnectTracker.prevConnected)
                    reconnectTracker.streak++;
                else if (senderStats.isPeerConnected)
                    reconnectTracker.streak = 0;
                reconnectTracker.prevConnected = senderStats.isPeerConnected;
                stats.peerReconnectStreak = reconnectTracker.streak;
            };
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
                    copyStats(lastStats, sender);
                try { sender?.dispose?.(); } catch { /* ignore */ }
                sender = null;
                initSent = false;
                pumpFailure = null;
                pumpResolved = false;
                warnLog?.log(`wireSend: resetting wire sender — ${reason}`);
            };
            // Per-layer "last keyframe's Index" — the value we stamp into
            // `keyFrameIndex` on every encoded frame. A frame is a keyframe
            // iff its `keyFrameIndex === index`.
            const lastKfIndexByLayer = new Map<number, number>();
            try {
                for await (const bundle of source) {
                    if (abortSignal?.aborted)
                        return;

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
                        // Snapshot the bundle's drop trace once per bundle: every
                        // per-layer DTO ships the same bytes so the server-side
                        // detector sees coherent gap counts on each layer's stream.
                        const dropTraceBytes = bundle.dropTrace.length > 0
                            ? Uint8Array.from(bundle.dropTrace)
                            : undefined;

                        const cur = controller.current.configs;
                        // Stable canonical ids: layerCount is the ladder CEILING
                        // (max id + 1), layerMask marks which tiers are live —
                        // legacy receivers read only layerCount, which stays
                        // consistent because ids never renumber.
                        let layerCount = 0;
                        let layerMask = 0;
                        for (let i = 0; i < cur.length; i++) {
                            const id = cur[i].layerId ?? i;
                            layerMask |= 1 << id;
                            if (id + 1 > layerCount)
                                layerCount = id + 1;
                        }
                        // One backing buffer for all layers' bytes: copy each chunk
                        // into its own slice. Saves N-1 ArrayBuffer allocations per
                        // bundle (the slices stay live together until serialized).
                        const dataPool = new ArrayBuffer(
                            bundle.layers.reduce((sum, e) => sum + e.chunk.byteLength, 0));
                        let dataOffset = 0;
                        const wireLayers: VideoStreamFrame[] = bundle.layers.map(encoded => {
                            const isKey = encoded.chunk.type === 'key';
                            if (isKey)
                                lastKfIndexByLayer.set(encoded.layerId, encoded.index);
                            // Sentinel for pre-keyframe deltas (pool-reused encoder
                            // can emit one before its first 'key'): `-1 !== index`
                            // keeps server + receiver from mis-classifying it as KF.
                            const keyFrameIndex = lastKfIndexByLayer.get(encoded.layerId) ?? -1;
                            const data = new Uint8Array(dataPool, dataOffset, encoded.chunk.byteLength);
                            encoded.chunk.copyTo(data);
                            dataOffset += encoded.chunk.byteLength;
                            const dto: VideoStreamFrame = {
                                offset,
                                offsetEpoch: capturedAt.epoch,
                                duration: microsecondsToTicks(encoded.chunk.duration ?? 0),
                                keyFrameIndex,
                                index: encoded.index,
                                width: encoded.encodedWidth,
                                height: encoded.encodedHeight,
                                data,
                                layerId: encoded.layerId,
                                layerCount,
                                layerMask,
                            };
                            if (encoded.rotation !== 0)
                                dto.rotation = encoded.rotation;
                            if (dropTraceBytes)
                                dto.dropTrace = dropTraceBytes;
                            if (isKey && encoded.description)
                                dto.description = encoded.description;
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
                            const description = top.description ?? null;
                            const topCfg = cur[cur.length - 1];
                            sender.init({
                                codec: top.metadata.decoderConfig?.codec ?? topCfg.codec,
                                width: topCfg.width,
                                height: topCfg.height,
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
                        // Terminal sender stage: aggregate this bundle's drop
                        // trail and count the bundle as shipped.
                        aggregateDropTrace(bundle.stats, bundle.dropTrace);
                        bundle.stats.bundlesShipped++;
                        copyStats(bundle.stats, sender);
                    } finally {
                        disposeEncodedBundle(bundle);
                    }
                }
            } finally {
                if (sender && lastStats)
                    copyStats(lastStats, sender);
                try { sender?.dispose?.(); } catch { /* ignore */ }
                // dispose() only MARKS the stream complete; $sys.End is sent by
                // the async sender pump. The worker is terminate()d right after
                // this pipe drains, so without awaiting the flush the socket
                // dies first and the server sees a disconnect (5s reconnect
                // grace → frozen remote tiles) instead of a graceful end.
                if (sender?.whenDisposed)
                    await Promise.race([
                        sender.whenDisposed.catch(() => undefined),
                        new Promise<void>(resolve => setTimeout(resolve, WIRE_END_FLUSH_TIMEOUT_MS)),
                    ]);
            }
        }
    };
}

// Chunked Latin-1 base64: workers don't expose Buffer, and HVCC/SPS inputs
// can overflow String.fromCharCode(...) in one shot.
function bytesToBase64(bytes: Uint8Array): string {
    if (bytes.byteLength === 0)
        return '';

    const CHUNK = 0x8000;
    let s = '';
    for (let i = 0; i < bytes.byteLength; i += CHUNK) {
        const slice = bytes.subarray(i, Math.min(i + CHUNK, bytes.byteLength));
        s += String.fromCharCode(...slice);
    }
    return btoa(s);
}
