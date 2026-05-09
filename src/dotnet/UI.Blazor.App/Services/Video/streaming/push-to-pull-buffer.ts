/**
 * Capture-side push → RPC-pull bridge for the recording pipeline.
 *
 * Sits between the synchronous `send(bundle)` calls coming out of
 * `wireSend` and the async pull driven by Fusion's
 * `RpcStreamSender`. A small `Denque<VideoStreamFrameBundle>`
 * (≈ 1 s capacity at frameRate) marries the two — under healthy
 * operation the queue stays near-empty because the RPC sender's
 * pump runs continuously while connected, draining bundles into its
 * own large local ring (`VIDEO.senderBufferSize`).
 *
 * Backpressure is propagated to the capture source via a
 * {@link FloodGate}: when the bundle queue reaches half capacity
 * the gate closes (the `floodGate` operator immediately after
 * `mstpSource` drops captured frames), and reopens when the queue
 * drains below a quarter — hysteresis keeps it from flapping.
 *
 * - `ensureRpcPush` lazily binds the worker's `Api.hub` default peer
 *   and caches the `ILiveVideoStreams` client on the
 *   {@link StreamingContext}.
 * - `createWireSender` returns a {@link StreamSenderLike} that
 *   `wireSend` drives. Internally it pumps the rendezvous Denque into
 *   `RpcStream<VideoFrameBundleDto>` started via
 *   `streamingApi.liveVideoStreams.PushStream(...)`.
 * - `createPullStream` is the receiver-side counterpart — a thin
 *   wrapper over `streamingApi.liveVideoStreams.GetStream(...)`
 *   shaped to fit `pullSource`'s `getStream` factory.
 */

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { RpcStream } from 'actuallab-rpc';
import { VIDEO } from 'app-constants';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment,
    type LiveVideoStreamsClient, type SessionTokenProvider, type VideoFormatDto,
    type VideoFrameBundleDto, type VideoFrameDto } from 'api';
import { ServerClock } from 'clocks';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import type { FloodGate } from '../operators/flood-gate';
import type {
    StreamSenderLike,
    StreamSenderStats,
    VideoStreamFrame,
    VideoStreamFrameBundle,
} from '../operators/wire-send';

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
function bundleToDto(bundle: VideoStreamFrameBundle): VideoFrameBundleDto {
    return { Layers: bundle.layers.map(frameToDto) };
}

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
    /** Backpressure valve toggled by this module's queue-fill watcher.
     *  Closed when `bundles.length >= pushPullBufferSize / 2`, opened
     *  when it drops below `pushPullBufferSize / 4`. */
    floodGate: FloodGate;
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
    rpcStreamSkipped: number;
    floodGateSkipCount: number;
    lastAckAgeMs: number;
    isPeerConnected: boolean;
}

export interface DisposableStreamSender extends StreamSenderLike {
    dispose(): void;
    readonly whenDisposed: Promise<void>;
    getStats(): WireSenderStats;
}

/**
 * Build a production wire sender that pumps bundles into
 * `streamingApi.liveVideoStreams.PushStream(...)` over Fusion RPC.
 *
 * Internals:
 *
 *  - A `Denque<VideoStreamFrameBundle>` of capacity ≈ 1 s
 *    (`VIDEO.pushPullBufferSize`) is the rendezvous between the sync
 *    `send(bundle)` calls and the async `RpcStream` source iterator.
 *    Under healthy operation the queue stays near-empty because Fusion's
 *    pump fills its own large ring (`VIDEO.senderBufferSize`) continuously
 *    while connected, draining bundles out of this Denque immediately.
 *
 *  - When the queue fills to half capacity, {@link CreateWireSenderOptions.floodGate}
 *    is closed — the capture-side `floodGate` operator drops captured
 *    frames at the source. The gate reopens once the queue drains below
 *    a quarter capacity (hysteresis avoids flapping).
 *
 *  - An `EventHandlerSet<void>` is used to wake the RpcStream pump on
 *    every push, plus once at end-of-stream when `dispose()` flips
 *    `isCompleted`.
 *
 *  - Termination is driven by the iterator returning. Fusion's
 *    `RpcStreamSender.disconnect()` calls `.return()` on the generator
 *    when needed; calling `dispose()` from the pipeline side flips
 *    `isCompleted` and triggers the wake event so the generator returns
 *    naturally.
 */
export function createWireSender(opts: CreateWireSenderOptions): DisposableStreamSender {
    const { chatId, format, sourceStartedAtMs, streamingContext: ctx, floodGate } = opts;

    const queueCapacity = VIDEO.pushPullBufferSize;
    const closeGateAt = Math.floor(queueCapacity / 2);
    // +1 to keep the open threshold strictly below close, even when
    // queueCapacity = 4 (close=2, open=1) and below.
    const openGateAt = Math.max(0, Math.floor(queueCapacity / 4) - 1);

    const bundles = new Denque<VideoStreamFrameBundle>();
    const frameAdded = new EventHandlerSet<void>();
    let addedFrameCount = 0;
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
            const stream = new RpcStream<VideoFrameBundleDto>(
                (async function* () {
                    for (;;) {
                        while (!bundles.isEmpty()) {
                            const bundle = bundles.shift()!;
                            // Shifting may have crossed the open threshold —
                            // re-open the flood gate so capture resumes.
                            if (!floodGate.isOpen && bundles.length <= openGateAt)
                                floodGate.open();
                            yield bundleToDto(bundle);
                        }
                        if (isCompleted) return;
                        await frameAdded.whenNextVoid();
                    }
                })(),
                // canSkipTo: a bundle is a decode-anchor iff its first frame
                // is a keyframe. applyKeyframePolicy enforces all-or-none
                // across a bundle's layers, so any frame's IsKeyFrame would
                // do; we use Layers[0] to keep it cheap.
                MediaRpcStreamOptions.videoRealtime<VideoFrameBundleDto>(
                    bundle => bundle.Layers.length > 0 && bundle.Layers[0].IsKeyFrame),
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

    const send = (bundle: VideoStreamFrameBundle): void => {
        if (isCompleted) return;
        // Late frames after pump teardown: drop silently. Throwing here
        // races with the wire-send operator's whenDisposed handler — when
        // PushStream rejects, isDisposed flips synchronously while the
        // pump's reject microtask is still pending, so one in-flight
        // frame would otherwise mask the real error with the generic
        // "send after stream disposed" message.
        if (isDisposed) return;
        if (bundle.layers.length === 0) return;
        // Treat a bundle whose every layer has empty data as no-op too.
        let totalBytes = 0;
        for (const f of bundle.layers) totalBytes += f.data.byteLength;
        if (totalBytes === 0) return;

        bundles.push(bundle);
        addedFrameCount += bundle.layers.length;
        const isKeyBundle = bundle.layers[0].isKeyFrame;
        if (bundles.length > maxQueueDepth)
            maxQueueDepth = bundles.length;
        // Capture-side backpressure: when the queue fills to half
        // capacity, ask the flood gate to drop captured frames at the
        // source. The pump's shift loop reopens the gate when the queue
        // drains below `openGateAt` (hysteresis prevents flapping).
        if (floodGate.isOpen && bundles.length >= closeGateAt)
            floodGate.close();

        // Heartbeat at Info so live diagnosis doesn't need a log-level
        // override. At 30fps this fires roughly every 10s.
        if (addedFrameCount <= 3 || addedFrameCount % 300 === 0) {
            infoLog?.log(`send: total=${addedFrameCount} queueBundles=${bundles.length} ` +
                `peakQueueBundles=${maxQueueDepth} ` +
                `floodGateSkipCount=${floodGate.skipCount} gateOpen=${floodGate.isOpen} ` +
                `isKey=${isKeyBundle} bundleLayers=${bundle.layers.length} size=${totalBytes} ` +
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
        queueDepth: bundles.length,
        maxQueueDepth,
        rpcStreamSkipped: streamSkipped(),
        floodGateSkipCount: floodGate.skipCount,
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
