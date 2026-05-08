/**
 * Production glue between the new video pipeline (`Services/Video/operators/`)
 * and the Fusion `actuallab-rpc` machinery.
 *
 * - `ensureRpcPush` lazily binds the worker's `Api.hub` default peer and
 *   caches the `ILiveVideoStreams` client on the {@link StreamingContext}.
 * - `createWireSender` returns a {@link StreamSenderLike} that the
 *   `wireSend` operator drives — internally it pumps a `Denque` rendezvous
 *   into an `RpcStream<VideoFrameDto>` started via
 *   `streamingApi.liveVideoStreams.PushStream(...)`. Mirrors the legacy
 *   `InternalVideoStream` from `VideoOld/workers/video-streaming.ts`.
 * - `createPullStream` is the receiver-side counterpart: a thin wrapper
 *   over `streamingApi.liveVideoStreams.GetStream(...)` shaped to fit
 *   `pullSource`'s `getStream` factory.
 *
 * This file does NOT depend on `Services/VideoOld/` — the relevant bits
 * (the `frameToDto` mapper and the producer/consumer loop) are inlined
 * here so the new pipeline can stand on its own.
 */

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { RpcStream } from 'actuallab-rpc';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment,
    type LiveVideoStreamsClient, type SessionTokenProvider, type VideoFormatDto, type VideoFrameDto } from 'api';
import { ServerClock } from 'clocks';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import type { StreamSenderLike, StreamSenderStats, VideoStreamFrame } from '../operators/wire-send';

const { infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

/** Session token used by PushStream / GetStream — `'~'` = Session.Default,
 *  resolved from the WebSocket connection context. */
const RPC_SESSION_DEFAULT = '~';

/**
 * Per-worker shared state for the video streaming RPC client.
 *
 * Mirrors `VideoOld`'s shape minus the `processing` flag — the new
 * pipeline drives its lifecycle through the `StreamContext` / `runStream`
 * abort surface rather than a manual flag.
 */
export interface StreamingContext {
    chatId: string;
    serverClockOffsetMs: number;
    /** 0 = Camera, 1 = ScreenCast. Matches `.NET VideoSourceKind`. */
    sourceKind: number;
    /** Fusion RPC WebSocket URL. Mirrored into `Api.hub.defaultPeerUrl` on
     *  first push via {@link ensureRpcPush}. */
    apiUrl: string | null;
    sessionTokenProvider?: SessionTokenProvider;
    /** Lazily-constructed `ILiveVideoStreams` RPC client, bound to the hub's
     *  default peer. */
    rpcLiveVideoStreams: LiveVideoStreamsClient | null;
}

export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

export function serverClockNow(_ctx: StreamingContext): number {
    return ServerClock.now();
}

/**
 * Convert a worker-side `VideoStreamFrame` into the MessagePack map shape
 * expected by `.NET VideoFrame` (`[MessagePackObject(true)]` ⇒ PascalCase keys).
 *
 * Inlined verbatim from `VideoOld/workers/video-streaming.ts`.
 */
function frameToDto(frame: VideoStreamFrame): VideoFrameDto {
    // @msgpack/msgpack v3 emits large JS numbers as float64 once they exceed
    // uint32. The server reads TimeSpan ticks as int64, so pass bigint to force
    // an integer msgpack code for offsets past 429.4967295s.
    const dto: VideoFrameDto = {
        Data: frame.data,
        Offset: toMoment(frame.offset),
        Duration: toMoment(frame.duration),
        IsKeyFrame: frame.isKeyFrame,
    };
    if (frame.offsetEpoch !== undefined)
        dto.OffsetEpoch = frame.offsetEpoch;
    if (frame.isKeyFrame) {
        dto.Width = frame.width;
        dto.Height = frame.height;
        if (frame.sourceWidth && frame.sourceHeight) {
            dto.SourceWidth = frame.sourceWidth;
            dto.SourceHeight = frame.sourceHeight;
        }
    }
    if (frame.description) dto.Description = frame.description;
    if (frame.codec) dto.Codec = frame.codec;
    if (frame.temporalLayerId !== undefined && frame.temporalLayerId > 0)
        dto.TemporalLayerId = frame.temporalLayerId;
    if (frame.layerId !== undefined && frame.layerId > 0)
        dto.LayerId = frame.layerId;
    // Always emit the producer's current ladder max; the server's
    // ReceiveQualityFilter clamps the consumer cap without observing layers
    // over time.
    dto.MaxLayerId = frame.maxLayerId ?? 0;
    return dto;
}

/**
 * Lazily initialise the Fusion RPC push peer for the worker context by
 * configuring the shared `Api.hub`'s default peer. The `ILiveVideoStreams`
 * client is cached on the {@link StreamingContext} so every wire sender
 * created from the same context shares the same WebSocket for the life of
 * the worker.
 */
export function ensureRpcPush(ctx: StreamingContext): void {
    if (ctx.rpcLiveVideoStreams)
        return;

    if (!ctx.apiUrl)
        throw new Error('Fusion RPC push: apiUrl is not set');

    Api.init('VideoStreaming', {
        url: ctx.apiUrl,
        modules: [streamingApi],
        connectivityUI: WorkerConnectivityUI,
        sessionTokenProvider: ctx.sessionTokenProvider,
        // Mirror the audio worker's pattern. Without this the
        // peer's `_canConnect` flag stays false (requiresConnection=false
        // ⇒ `requiresConnection && isDotNetRpcConnected = false`), and
        // `StreamPushMode` calls (which include `AwaitForConnection`)
        // hang in the delayed-call queue forever — the server never
        // sees them. The audio worker also sets this.
        requireConnection: true,
    });
    // Api.init is a no-op if the hub was already created (e.g. by
    // the main app's bootstrap on the same realm). In that case the
    // call above silently drops `requireConnection: true` — re-add
    // the scope explicitly so PushStream's `AwaitForConnection`
    // can resolve.
    Api.requireConnection('VideoStreaming');
    ctx.rpcLiveVideoStreams = streamingApi.liveVideoStreams;
}

/** Keyframe metadata used to start `PushStream`. Matches `VideoFormatDto`
 *  field-for-field (camelCased on the worker side; converted to the server
 *  PascalCase shape inside {@link createWireSender}). */
export interface WireSenderFormat {
    codec: string;
    width: number;
    height: number;
    sourceWidth: number;
    sourceHeight: number;
    codecSettings: string;
}

export interface CreateWireSenderOptions {
    chatId: string;
    format: WireSenderFormat;
    sourceStartedAtMs: number;
    streamingContext: StreamingContext;
}

/** Sender returned by {@link createWireSender}. The pipeline calls
 *  `dispose()` to signal end-of-stream so `PushStream`'s iterator returns
 *  and the underlying `RpcStream` completes its drain.
 *
 *  `whenDisposed` resolves on clean drain and rejects with the pump's
 *  failure (PushStream rejection / generator throw) — letting the
 *  pipeline observe a backend failure that the sync `send()` path
 *  can't surface on its own. */
/** Snapshot of {@link createWireSender}'s queue / drop counters. The
 *  recorder's quality controller can read this to feed bitrate / layer
 *  decisions; under healthy conditions every counter except
 *  `addedFrameCount` stays at zero. */
export interface WireSenderStats extends StreamSenderStats {
    /** Total frames pushed into the bridge. */
    addedFrameCount: number;
    /** Current queue depth (frames awaiting pump pickup). */
    queueDepth: number;
    /** Highest queue depth observed across the run. */
    maxQueueDepth: number;
    /** Frames dropped by the overflow-compaction policy. */
    droppedAtSenderQueue: number;
    /** Subset of `droppedAtSenderQueue` that were keyframes (severe loss). */
    droppedKeyframesAtSenderQueue: number;
    rpcStreamSkipped: number;
    lastAckAgeMs: number;
    isPeerConnected: boolean;
}

export interface DisposableStreamSender extends StreamSenderLike {
    dispose(): void;
    readonly whenDisposed: Promise<void>;
    getStats(): WireSenderStats;
}

/**
 * Build a production wire sender that pumps frames into
 * `streamingApi.liveVideoStreams.PushStream(...)` over Fusion RPC.
 *
 * Internals mirror `VideoOld`'s `InternalVideoStream`:
 *
 *  - A `Denque<VideoStreamFrame>` is the producer/consumer rendezvous
 *    between the (sync) `send(dto)` calls and the (async) `RpcStream`
 *    source iterator. It is NOT an intentional buffer: under healthy
 *    operation it stays near-empty because `RpcStream`'s ring buffer plus
 *    its `canSkipTo` ACK compaction is the only buffer for unsent encoded
 *    frames. Backpressure is handled upstream (encoder slot replacement).
 *
 *  - An `EventHandlerSet<void>` is used to wake the RpcStream pump on
 *    every push, plus once at end-of-stream when `dispose()` flips
 *    `isCompleted`.
 *
 *  - Termination is driven by the iterator returning. `Fusion`'s
 *    `RpcStreamSender.disconnect()` calls `.return()` on the generator
 *    when needed; calling `dispose()` from the pipeline side flips
 *    `isCompleted` and triggers the wake event so the generator returns
 *    naturally.
 */
export function createWireSender(opts: CreateWireSenderOptions): DisposableStreamSender {
    const { chatId, format, sourceStartedAtMs, streamingContext: ctx } = opts;

    const frames = new Denque<VideoStreamFrame>();
    const frameAdded = new EventHandlerSet<void>();
    let addedFrameCount = 0;
    let droppedAtSenderQueue = 0;
    let droppedKeyframesAtSenderQueue = 0;
    let maxQueueDepth = 0;
    let rpcStreamSkipped = 0;
    let rpcStreamSender: { readonly skipCount: number; onAckProcessed?: () => void } | null = null;
    let lastAckAtMs = -1;
    let isCompleted = false;
    let isDisposed = false;
    let lastError = '';
    let pumpReject: (err: Error) => void = () => { /* set below */ };
    let pumpResolve: () => void = () => { /* set below */ };
    const whenDisposed = new Promise<void>((resolve, reject) => {
        pumpResolve = resolve;
        pumpReject = reject;
    });
    // Pre-attach a no-op catch so the rejection isn't unhandled when
    // the caller observes it via a separate await.
    whenDisposed.catch(() => { /* observed externally */ });

    const pump = async (): Promise<void> => {
        try {
            ensureRpcPush(ctx);
            const liveVideoStreams = ctx.rpcLiveVideoStreams!;
            const peer = Api.peer;

            const sourceStartOffsetSeconds = sourceStartedAtMs / 1000;
            infoLog?.log(`createWireSender: sourceStartOffset=${sourceStartOffsetSeconds.toFixed(3)}s ` +
                `(sourceStartedAtMs=${sourceStartedAtMs.toFixed(0)})`);

            infoLog?.log(`createWireSender: PushStream codec=${format.codec}, ` +
                `${format.width}x${format.height} ` +
                `(source ${format.sourceWidth}x${format.sourceHeight}), ` +
                `settings=${format.codecSettings.length} chars`);

            const formatDto: VideoFormatDto = {
                Codec: format.codec,
                CodecSettings: format.codecSettings,
                Size: { Width: format.width, Height: format.height },
                SourceSize: { Width: format.sourceWidth, Height: format.sourceHeight },
            };

            // Real-time video stream: isRealTime=true, allowReconnect=true,
            // keyframe-safe compaction. With allowReconnect=true, a same-peer
            // WS reconnect keeps the sender alive and the
            // real-time-skip-to-keyframe logic in Fusion's RpcSharedStream/
            // Sender drives resume via $sys.Ack(MustReset=true). On peer-change
            // the sender is disposed via sharedObjects.disconnectAll().
            //
            // Termination is driven by iterator.return() — Fusion's
            // RpcStreamSender.disconnect() calls it on the generator, which
            // unwinds any try/finally blocks and exits.
            const stream = new RpcStream<VideoFrameDto>(
                (async function* () {
                    for (;;) {
                        while (!frames.isEmpty()) {
                            yield frameToDto(frames.shift()!);
                        }
                        if (isCompleted) return;
                        await frameAdded.whenNextVoid();
                    }
                })(),
                MediaRpcStreamOptions.videoRealtime<VideoFrameDto>(
                    frame => frame.IsKeyFrame && (frame.LayerId ?? 0) === 0),
            );

            const streamRef = stream.toRef(peer);
            if (stream.sender) {
                rpcStreamSender = stream.sender;
                rpcStreamSender.onAckProcessed = () => {
                    lastAckAtMs = Date.now();
                    rpcStreamSkipped = rpcStreamSender?.skipCount ?? rpcStreamSkipped;
                };
            }

            void liveVideoStreams
                .PushStream(RPC_SESSION_DEFAULT, chatId, sourceStartOffsetSeconds, formatDto, ctx.sourceKind, streamRef)
                .catch((err: unknown) => {
                    const msg = err instanceof Error ? err.message : String(err);
                    warnLog?.log('PushStream rejected:', err);
                    lastError = `PushStream rejected: ${msg}`;
                })
                .finally(() => stream.disconnect());

            // Wait for the pump to complete (writeFrom was started by toRef).
            await stream.whenSent;
        } catch (error) {
            const msg = error instanceof Error ? error.message : String(error);
            errorLog?.log('createWireSender error:', error);
            lastError = `stream error: ${msg}`;
        } finally {
            isDisposed = true;
            if (lastError)
                pumpReject(new Error(lastError));
            else
                pumpResolve();
        }
    };

    void pump();

    // Queue-overflow drop policy. The Denque is a sync→async bridge: under
    // healthy conditions the pump drains it as fast as send() pushes.
    // Under sustained pump stall (peer disconnected, ACKs blocked, slow
    // network), the Denque is the only place push-rate vs ACK-rate
    // mismatch can accumulate — RpcStream's own RingBuffer is downstream
    // and only fills when the iterator yields, which the pump-stall
    // prevents. We can't use a ReplaceableSlot here: encoded frames are
    // not freely droppable (delta sequences depend on prior keyframes,
    // and we have multiple layers).
    //
    // Trigger: any single layer has > 2 keyframes queued — a buffer this
    // deep means we're past one full keyframe cycle plus an in-progress
    // one, which only happens when the pump can't move forward.
    // Action: drop everything before the most recent keyframe in the
    // queue. The receiver can resync onto a keyframe regardless of
    // layer, so this preserves decodability for whatever survives.
    const compactIfOverflowing = (): void => {
        let lastKfIdx = -1;
        const kfCountByLayer = new Map<number, number>();
        for (let i = 0; i < frames.length; i++) {
            const f = frames.get(i)!;
            if (!f.isKeyFrame) continue;
            const layer = f.layerId ?? 0;
            kfCountByLayer.set(layer, (kfCountByLayer.get(layer) ?? 0) + 1);
            lastKfIdx = i;
        }
        let maxKfPerLayer = 0;
        for (const c of kfCountByLayer.values())
            if (c > maxKfPerLayer) maxKfPerLayer = c;

        if (maxKfPerLayer <= 2 || lastKfIdx <= 0) return;

        let droppedKeys = 0;
        for (let i = 0; i < lastKfIdx; i++) {
            const dropped = frames.shift()!;
            if (dropped.isKeyFrame) droppedKeys++;
        }
        droppedAtSenderQueue += lastKfIdx;
        droppedKeyframesAtSenderQueue += droppedKeys;
        warnLog?.log(`send: queue compacted — dropped ${lastKfIdx} frames ` +
            `(incl. ${droppedKeys} keyframes); cumulative dropped=${droppedAtSenderQueue} ` +
            `keyframes=${droppedKeyframesAtSenderQueue}`);
    };

    const send = (frame: VideoStreamFrame): void => {
        if (isCompleted) return;
        // Late frames after pump teardown: drop silently. Throwing here
        // races with the wire-send operator's whenDisposed handler — when
        // PushStream rejects, isDisposed flips synchronously while the
        // pump's reject microtask is still pending, so one in-flight
        // frame would otherwise mask the real error with the generic
        // "send after stream disposed" message.
        if (isDisposed) return;
        if (frame.data.byteLength === 0) return;

        frames.push(frame);
        addedFrameCount++;
        if (frame.isKeyFrame)
            compactIfOverflowing();
        if (frames.length > maxQueueDepth)
            maxQueueDepth = frames.length;

        // Heartbeat at Info so live diagnosis doesn't need a log-level
        // override. At 30fps this fires roughly every 10s. Throws into
        // warnLog territory automatically via compactIfOverflowing when
        // the queue actually overflows.
        if (addedFrameCount <= 3 || addedFrameCount % 300 === 0) {
            infoLog?.log(`send: total=${addedFrameCount} queue=${frames.length} ` +
                `peakQueue=${maxQueueDepth} ` +
                `dropped=${droppedAtSenderQueue} keyframesDropped=${droppedKeyframesAtSenderQueue} ` +
                `isKey=${frame.isKeyFrame} size=${frame.data.byteLength} ` +
                `lastError='${lastError}' isDisposed=${isDisposed}`);
        }

        frameAdded.trigger();
    };

    const dispose = (): void => {
        if (isCompleted) return;
        isCompleted = true;
        frameAdded.trigger();
    };

    const getStats = (): WireSenderStats => ({
        addedFrameCount,
        queueDepth: frames.length,
        maxQueueDepth,
        droppedAtSenderQueue,
        droppedKeyframesAtSenderQueue,
        rpcStreamSkipped: streamSkipped(),
        lastAckAgeMs: lastAckAtMs >= 0 ? Date.now() - lastAckAtMs : -1,
        // RpcPeer.isConnected flips true only after the RPC handshake
        // completes (rpc-peer.ts: _setConnectionState(Connected) at the
        // post-handshake site). We additionally gate on the stream's own
        // PushStream call not having rejected — without this, an
        // auth/permission rejection at stream-create time would still
        // report "connected" because the RPC peer itself is healthy.
        isPeerConnected: Api.peer.isConnected && !lastError,
    });

    return { send, dispose, whenDisposed, getStats };

    function streamSkipped(): number {
        rpcStreamSkipped = Math.max(rpcStreamSkipped, rpcStreamSender?.skipCount ?? 0);
        return rpcStreamSkipped;
    }
}

/**
 * Receiver-side counterpart of {@link createWireSender}. Calls
 * `streamingApi.liveVideoStreams.GetStream('~', streamId)` and returns
 * the resulting iterable — shaped to fit `pullSource`'s `getStream`
 * factory.
 *
 * `ensureRpcPush(ctx)` is called first so the call uses the same shared
 * `Api.hub` peer that the push side uses.
 */
export function createPullStream(streamId: string, ctx: StreamingContext): Promise<AsyncIterable<VideoFrameDto>> {
    ensureRpcPush(ctx);
    const liveVideoStreams = ctx.rpcLiveVideoStreams!;
    return liveVideoStreams.GetStream(RPC_SESSION_DEFAULT, streamId);
}
