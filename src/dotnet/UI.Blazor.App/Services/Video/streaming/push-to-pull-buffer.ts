// Capture-side push → RPC-pull bridge between sync wireSend and Fusion's
// async RpcStreamSender. A small Denque (~1s capacity) is the rendezvous;
// FloodGate hysteresis (close at half, open below a quarter) feeds backpressure
// upstream to the capture source.

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { RpcStream } from 'actuallab-rpc';
import { computeUplinkAckAdvance, VIDEO } from 'app-constants';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment,
    type LiveVideoStreamsClient, type SessionTokenProvider, type VideoFormatDto,
    type VideoFrameBundleDto, type VideoFrameDto } from 'api';
import { ServerClock } from 'clocks';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import { isVideoFrameBundleDtoKeyFrame, isVideoStreamBundleKeyFrame } from '../bundle-helpers';
import type { FloodGate } from '../operators/flood-gate';
import type {
    StreamSenderLike,
    StreamSenderStats,
    VideoStreamFrame,
    VideoStreamFrameBundle,
} from '../operators/wire-send';

const { infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

// '~' = Session.Default, resolved from the WebSocket connection context.
const RPC_SESSION_DEFAULT = '~';

// Last measured uplink min-RTT (ms, -1 = never measured). Survives stream
// restarts so the next stream's credit window is sized for the real link.
let lastUplinkMinRttMs = -1;

export interface StreamingContext {
    chatId: string;
    serverClockOffsetMs: number;
    // 0 = Camera, 1 = ScreenCast. Matches .NET VideoSourceKind.
    sourceKind: number;
    apiUrl: string | null;
    sessionTokenProvider?: SessionTokenProvider;
    rpcLiveVideoStreams: LiveVideoStreamsClient | null;
}

export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

export function serverClockNow(_ctx: StreamingContext): number {
    return ServerClock.now();
}

function bundleToDto(bundle: VideoStreamFrameBundle): VideoFrameBundleDto {
    return { Layers: bundle.layers.map(frameToDto) };
}

function frameToDto(frame: VideoStreamFrame): VideoFrameDto {
    // @msgpack/msgpack v3 emits >uint32 JS numbers as float64; server reads
    // TimeSpan ticks as int64, so pass bigint to force int msgpack code beyond 429.4967295s.
    // IsKeyFrame is NOT on the wire — server derives it as KeyFrameIndex === Index.
    const isKey = frame.keyFrameIndex === frame.index;
    const dto: VideoFrameDto = {
        Data: frame.data,
        Offset: toMoment(frame.offset),
        Duration: toMoment(frame.duration),
        KeyFrameIndex: frame.keyFrameIndex,
        Index: frame.index,
    };
    if (frame.offsetEpoch !== undefined)
        dto.OffsetEpoch = frame.offsetEpoch;
    if (isKey) {
        dto.Width = frame.width;
        dto.Height = frame.height;
    }
    if (frame.description)
        dto.Description = frame.description;
    if (frame.codec)
        dto.Codec = frame.codec;
    if (frame.layerId !== undefined && frame.layerId > 0)
        dto.LayerId = frame.layerId;
    // Always emit producer's canonical ladder size — server's
    // ReceiveQualityFilter resolves the consumer cap without observing layers
    // over time. LayerMask marks which canonical tiers are currently encoded.
    dto.LayerCount = frame.layerCount ?? 1;
    if (frame.layerMask)
        dto.LayerMask = frame.layerMask;
    if (frame.dropTrace && frame.dropTrace.byteLength > 0)
        dto.DropTrace = frame.dropTrace;
    if (frame.rotation !== undefined && frame.rotation !== 0)
        dto.Rotation = frame.rotation;
    return dto;
}

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
        // Without this the peer's _canConnect stays false and StreamPushMode's
        // AwaitForConnection hangs in the delayed-call queue forever.
        requireConnection: true,
    });
    // Api.init is a no-op if the hub already exists; re-add the scope
    // explicitly so PushStream's AwaitForConnection can resolve.
    Api.requireConnection('VideoStreaming');
    ctx.rpcLiveVideoStreams = streamingApi.liveVideoStreams;
}

// Mirrors VideoFormatDto field-for-field (camelCased; PascalCased inside createWireSender).
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
    floodGate: FloodGate;
}

// Under healthy conditions every counter except addedFrameCount stays at zero.
export interface WireSenderStats extends StreamSenderStats {
    addedFrameCount: number;
    queueDepth: number;
    maxQueueDepth: number;
    rpcStreamSkipped: number;
    floodGateSkipCount: number;
    lastAckAgeMs: number;
    minRttMs: number;
    ringDepth: number;
    isPeerConnected: boolean;
}

export interface DisposableStreamSender extends StreamSenderLike {
    dispose(): void;
    readonly whenDisposed: Promise<void>;
    getStats(): WireSenderStats;
}

export function createWireSender(opts: CreateWireSenderOptions): DisposableStreamSender {
    const { chatId, format, sourceStartedAtMs, streamingContext: ctx, floodGate } = opts;

    const queueCapacity = VIDEO.pushPullBufferSize;
    const closeGateAt = Math.floor(queueCapacity / 2);
    // -1 keeps open strictly below close even at queueCapacity=4 (close=2, open=1).
    const openGateAt = Math.max(0, Math.floor(queueCapacity / 4) - 1);

    const bundles = new Denque<VideoStreamFrameBundle>();
    const frameAdded = new EventHandlerSet<void>();
    let addedFrameCount = 0;
    // Cumulative bytes drained out of this buffer (handed to the RpcStream as it
    // pulls). The pull is gated by the RPC ack-window, so while this buffer is
    // backlogged the delta/time ≈ the wire delivery rate = uplink capacity.
    let shiftedBytes = 0;
    let maxQueueDepth = 0;
    let rpcStreamSkipped = 0;
    let rpcStreamSender: {
        readonly skipCount: number;
        readonly minRttMs: number;
        onAckProcessed?: () => void;
        onBuffered?: (bufferedCount: number) => void;
    } | null = null;
    let ringDepth = 0;
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
    // No-op catch so the rejection isn't unhandled when caller observes it via a separate await.
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

            const streamOptions = {
                ...MediaRpcStreamOptions.videoRecording<VideoFrameBundleDto>(isVideoFrameBundleDtoKeyFrame),
                ackAdvance: computeUplinkAckAdvance(
                    VIDEO.frameRate, lastUplinkMinRttMs, VIDEO.rpcStreamAckAdvance, VIDEO.senderBufferSize),
            };
            if (streamOptions.ackAdvance !== VIDEO.rpcStreamAckAdvance)
                infoLog?.log(`createWireSender: ackAdvance=${streamOptions.ackAdvance} ` +
                    `(minRtt=${lastUplinkMinRttMs.toFixed(0)}ms)`);

            // allowReconnect=true keeps the sender alive across same-peer WS
            // reconnects; resume via $sys.Ack(MustReset=true). On peer-change,
            // disposed via sharedObjects.disconnectAll().
            const stream = new RpcStream<VideoFrameBundleDto>(
                (async function* () {
                    for (;;) {
                        while (!bundles.isEmpty()) {
                            const bundle = bundles.shift()!;
                            for (const f of bundle.layers)
                                shiftedBytes += f.data.byteLength;
                            if (!floodGate.isOpen && bundles.length <= openGateAt)
                                floodGate.open();
                            yield bundleToDto(bundle);
                        }

                        if (isCompleted)
                            return;

                        await frameAdded.whenNextVoid();
                    }
                })(),
                streamOptions,
            );

            const streamRef = stream.toRef(peer);
            if (stream.sender) {
                rpcStreamSender = stream.sender;
                rpcStreamSender.onAckProcessed = () => {
                    lastAckAtMs = Date.now();
                    rpcStreamSkipped = rpcStreamSender?.skipCount ?? rpcStreamSkipped;
                    const minRttMs = rpcStreamSender?.minRttMs ?? -1;
                    if (minRttMs >= 0)
                        lastUplinkMinRttMs = minRttMs;
                };
                // The ring absorbs ~4s of backlog before the Denque even starts
                // filling, so its occupancy is the EARLIEST outbound-backpressure
                // signal — queueDepth only moves after the ring saturates.
                rpcStreamSender.onBuffered = bufferedCount => {
                    ringDepth = bufferedCount;
                };
            }
            if (isCompleted) {
                stream.disconnect();
                await stream.whenSent;
                return;
            }

            void liveVideoStreams
                .PushStream(RPC_SESSION_DEFAULT, chatId, sourceStartOffsetSeconds, formatDto, ctx.sourceKind, streamRef)
                .catch((err: unknown) => {
                    const msg = err instanceof Error ? err.message : String(err);
                    warnLog?.log('PushStream rejected:', err);
                    lastError = `PushStream rejected: ${msg}`;
                })
                .finally(() => stream.disconnect());

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
        if (isCompleted)
            return;

        // Drop late frames silently — throwing would race with the wire-send
        // operator's whenDisposed handler and mask the real PushStream error.
        if (isDisposed)
            return;

        if (bundle.layers.length === 0)
            return;

        let totalBytes = 0;
        for (const f of bundle.layers)
            totalBytes += f.data.byteLength;
        if (totalBytes === 0)
            return;

        bundles.push(bundle);
        addedFrameCount += bundle.layers.length;
        // Bundle is a keyframe only when every layer agrees.
        const isKeyBundle = isVideoStreamBundleKeyFrame(bundle);
        if (bundles.length > maxQueueDepth)
            maxQueueDepth = bundles.length;
        if (floodGate.isOpen && bundles.length >= closeGateAt)
            floodGate.close();

        // Info-level heartbeat — fires every ~10s at 30fps without log-level override.
        if (addedFrameCount <= 3 || addedFrameCount % 300 === 0) {
            infoLog?.log(`send: total=${addedFrameCount} queueBundles=${bundles.length} ` +
                `peakQueueBundles=${maxQueueDepth} ` +
                `floodGateSkipCount=${floodGate.skipCount} gateOpen=${floodGate.isOpen} ` +
                `isKeyBundle=${isKeyBundle} bundleLayers=${bundle.layers.length} size=${totalBytes} ` +
                `lastError='${lastError}' isDisposed=${isDisposed}`);
        }

        frameAdded.trigger();
    };

    const dispose = (): void => {
        if (isCompleted)
            return;

        isCompleted = true;
        // Drop the backlog so $sys.End jumps the ack-window-gated queue — draining
        // a congested uplink first delays End past the stop flush budget (a frozen
        // remote tile until the disconnect watchdog fires). Trailing frames are moot.
        bundles.clear();
        frameAdded.trigger();
        // Don't call RpcStream.disconnect() here: it sets _ended=true on the
        // sender without sending $sys.End, leaving the server's consumer
        // waiting until KeepAlive eventually disconnects the orphan stream
        // (~10s). Instead, isCompleted=true lets the generator return done
        // naturally, which queues _endItem, fires sendEnd, and the server
        // sees graceful end-of-stream immediately. Recovery-on-eviction is
        // driven by pump rejection, not by this path.
    };

    const getStats = (): WireSenderStats => ({
        addedFrameCount,
        queueDepth: bundles.length,
        maxQueueDepth,
        rpcStreamSkipped: streamSkipped(),
        floodGateSkipCount: floodGate.skipCount,
        lastAckAgeMs: lastAckAtMs >= 0 ? Date.now() - lastAckAtMs : -1,
        minRttMs: rpcStreamSender?.minRttMs ?? -1,
        ringDepth,
        // Gate on !lastError too — without it, a stream-create rejection
        // (auth/permission) would still report connected because the RPC peer is healthy.
        isPeerConnected: Api.peer.isConnected && !lastError,
        ackedBytes: shiftedBytes,
    });

    return { send, dispose, whenDisposed, getStats };

    function streamSkipped(): number {
        rpcStreamSkipped = Math.max(rpcStreamSkipped, rpcStreamSender?.skipCount ?? 0);
        return rpcStreamSkipped;
    }
}
