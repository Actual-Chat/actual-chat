/**
 * In-worker video streaming over Fusion RPC binary transport.
 *
 * Sends encoded frames to the server via `ILiveVideoStreams.PushStream`, using
 * an `RpcStreamSender<VideoFrameDto>` to stream typed frame objects.
 */

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { RpcStream, type RpcStreamSender } from 'actuallab-rpc';
import { Api, streamingApi, toMoment,
    type LiveVideoStreamsClient, type SessionTokenProvider, type VideoFormatDto, type VideoFrameDto } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';
import { VIDEO } from 'app-constants';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

/** Session token used by PushStream — `'~'` = Session.Default, resolved from the
 *  WebSocket connection context (same trick as the pull side GetStream call). */
const RPC_SESSION_DEFAULT = '~';

export interface VideoStreamFrame {
    offset: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array;
    codec?: string;
    temporalLayerId?: number;
    // SVC spatial layer (simulcast): 0 = base (lowest-res), 1+ = higher-res layers.
    // Always 0 for single-encoder (P2P) streams.
    spatialLayerId?: number;
    // Spatial layers currently produced by the encoder ladder. The minimum is
    // always 0; max + top dimensions describe the available range.
    maxSpatialLayerId?: number;
    maxSpatialLayerWidth?: number;
    maxSpatialLayerHeight?: number;
    // Native source dimensions, populated on keyframes only. Sent to server so
    // it can track source-resolution growth (window resize, camera swap) and
    // unlock higher quality presets mid-stream without a full stream restart.
    sourceWidth?: number;
    sourceHeight?: number;
}

export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

export interface StreamingContext {
    chatId: string;
    serverClockOffsetMs: number;
    streamKind: number; // 0 = Webcam, 1 = Screencast
    processing: boolean;
    /** Fusion RPC WebSocket URL. Mirrored into `Api.hub.defaultPeerUrl` on first
     *  push via {@link ensureRpcPush}. */
    apiUrl: string | null;
    sessionTokenProvider?: SessionTokenProvider;
    /** Lazily-constructed `ILiveVideoStreams` RPC client, bound to the hub's
     *  default peer. */
    rpcLiveVideoStreams: LiveVideoStreamsClient | null;
}

export function serverClockNow(ctx: StreamingContext): number {
    return Date.now() + ctx.serverClockOffsetMs;
}

/**
 * Convert a worker-side `VideoStreamFrame` into the MessagePack map shape
 * expected by `.NET VideoFrame` (`[MessagePackObject(true)]` ⇒ PascalCase keys).
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
    if (frame.spatialLayerId !== undefined && frame.spatialLayerId > 0)
        dto.SpatialLayerId = frame.spatialLayerId;
    // Always emit the producer's current ladder top; the server's
    // ReceiveQualityFilter clamps the consumer cap into [0, max] per frame.
    dto.MaxSpatialLayerId = frame.maxSpatialLayerId ?? 0;
    dto.MaxSpatialLayerWidth = frame.maxSpatialLayerWidth ?? frame.width;
    dto.MaxSpatialLayerHeight = frame.maxSpatialLayerHeight ?? frame.height;
    return dto;
}

/**
 * Lazily initialise the Fusion RPC push peer for the worker context by
 * configuring the shared `Api.hub`'s default peer. The `ILiveVideoStreams`
 * client is cached on the `StreamingContext` so every `InternalVideoStream`
 * instance shares the same WebSocket for the life of the worker.
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
    });
    ctx.rpcLiveVideoStreams = streamingApi.liveVideoStreams;
}

/**
 * In-worker video stream producer. Pumps encoded frames to the server via
 * `IStreamServer.PushVideo` over Fusion RPC binary transport.
 *
 * The internal `frames` Denque is a producer/consumer rendezvous between
 * encoder output (sync, fire-and-forget) and the RpcStream source iterator
 * (async pull). It is NOT an intentional buffer: under healthy operation
 * it stays near-empty because RpcStream's RingBuffer (sized
 * `RpcStreamBufferSize + 1`) plus its real-time `canSkipTo` ACK compaction
 * is the only buffer for unsent encoded frames. Upstream encoder
 * backpressure (replaceable slot in video-processing.ts) handles sustained
 * overload by replacing pending frames; nothing here drops on its own.
 */
export class InternalVideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private addedFrameCount = 0;
    private _stream: RpcStream<VideoFrameDto> | null = null;

    public isCompleted = false;
    public isDisposed = false;
    public lastError = '';
    public readonly whenDisposed: Promise<void>;

    /** RpcStreamSender stats — populated once `stream()` has called `toRef()`.
     *  Returns null until then or after the stream is disposed. */
    get senderStats(): RpcStreamSender<VideoFrameDto> | null {
        const stream = this._stream;
        return stream?.sender ?? null;
    }

    constructor(
        private readonly config: {
            codec: string;
            width: number;
            height: number;
            sourceWidth: number;
            sourceHeight: number;
            maxSpatialLayerId: number;
            maxSpatialLayerWidth: number;
            maxSpatialLayerHeight: number;
            codecSettings: string;
        },
        private readonly ctx: StreamingContext,
        private readonly sourceStartedAtMs: number,
        streamAfter?: Promise<void>,
    ) {
        this.whenDisposed = this.stream(streamAfter);
    }

    addFrame(frame: VideoStreamFrame): void {
        if (this.isCompleted) return;
        if (frame.data.byteLength === 0) return;

        this.frames.push(frame);
        this.addedFrameCount++;

        if (this.addedFrameCount <= 3 || this.addedFrameCount % 300 === 0) {
            debugLog?.log(`addFrame: total: ${this.addedFrameCount} queue: ${this.frames.length} ` +
                `isKey: ${frame.isKeyFrame} size: ${frame.data.byteLength}`);
        }

        this.frameAdded.trigger();
    }

    getAddedFrameCount(): number {
        return this.addedFrameCount;
    }

    getQueueLength(): number {
        return this.frames.length;
    }

    complete(): void {
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    private async stream(streamAfter?: Promise<void>): Promise<void> {
        try {
            if (streamAfter) await streamAfter;
            if (!this.ctx.processing) return;

            ensureRpcPush(this.ctx);
            const liveVideoStreams = this.ctx.rpcLiveVideoStreams!;
            const peer = Api.peer;

            const clientStartOffset = this.sourceStartedAtMs / 1000;
            infoLog?.log(`stream: clientStartOffset=${clientStartOffset.toFixed(3)}s ` +
                `(sourceStartedAtMs=${this.sourceStartedAtMs.toFixed(0)})`);

            infoLog?.log(`stream: PushStream codec=${this.config.codec}, ` +
                `${this.config.width}x${this.config.height} ` +
                `(source ${this.config.sourceWidth}x${this.config.sourceHeight}), ` +
                `settings=${this.config.codecSettings.length} chars`);

            const format: VideoFormatDto = {
                Codec: this.config.codec,
                Width: this.config.width,
                Height: this.config.height,
                CodecSettings: this.config.codecSettings,
                SourceWidth: this.config.sourceWidth,
                SourceHeight: this.config.sourceHeight,
                MaxSpatialLayerId: this.config.maxSpatialLayerId,
                MaxSpatialLayerWidth: this.config.maxSpatialLayerWidth,
                MaxSpatialLayerHeight: this.config.maxSpatialLayerHeight,
            };

            // Real-time video stream: isRealTime=true, allowReconnect=true, ackPeriod=5, bufferSize=31.
            // With allowReconnect=true, a same-peer WS reconnect keeps the sender alive and the
            // real-time-skip-to-keyframe logic in Fusion's RpcSharedStream/Sender drives resume via
            // $sys.Ack(MustReset=true). On peer-change the sender is disposed via
            // sharedObjects.disconnectAll(); the outer video-processing.ts detects
            // videoStream.isDisposed and creates a fresh InternalVideoStream at the next keyframe.
            //
            // Termination is driven by iterator.return() — Fusion's RpcStreamSender.disconnect()
            // calls it on the generator, which unwinds any try/finally blocks and exits. Mirrors
            // .NET's IAsyncEnumerable/CancellationToken pattern in MauiRecorderEngine.SendAudio.
            // toRef() creates the sender, registers it, and starts pumping in the background.
            // eslint-disable-next-line @typescript-eslint/no-this-alias -- async generator function* can't be arrow
            const self = this;
            const stream = new RpcStream<VideoFrameDto>(
                (async function* () {
                    for (;;) {
                        while (!self.frames.isEmpty()) {
                            yield frameToDto(self.frames.shift()!);
                        }
                        if (self.isCompleted) return;
                        await self.frameAdded.whenNextVoid();
                    }
                })(),
                {
                    isRealTime: true,
                    allowReconnect: true,
                    ackPeriod: VIDEO.rpcStreamAckPeriod,
                    bufferSize: VIDEO.rpcStreamBufferSize,
                    canSkipTo: (frame) => frame.IsKeyFrame,
                },
            );
            this._stream = stream;

            // Fire-and-forget: server awaits the frameStream completion. Any
            // rejection is logged but shouldn't cancel the pump loop since the
            // sender owns the lifetime of the stream.
            void liveVideoStreams
                .PushStream(RPC_SESSION_DEFAULT, this.ctx.chatId, clientStartOffset, format, stream.toRef(peer), this.ctx.streamKind)
                .catch((err: unknown) => {
                    const msg = err instanceof Error ? err.message : String(err);
                    warnLog?.log('PushStream rejected:', err);
                    this.lastError = `PushStream rejected: ${msg}`;
                })
                .finally(() => stream.disconnect());

            // Wait for the pump to complete (writeFrom was started by toRef)
            await stream.whenSent;
        } catch (error) {
            const msg = error instanceof Error ? error.message : String(error);
            errorLog?.log('VideoStream error:', error);
            this.lastError = `stream error: ${msg}`;
        } finally {
            this.isDisposed = true;
        }
    }
}
