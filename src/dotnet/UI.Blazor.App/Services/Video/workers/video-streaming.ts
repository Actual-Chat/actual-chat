/**
 * In-worker video streaming over Fusion RPC binary transport.
 *
 * Sends encoded frames to the server via `IStreamServer.PushVideo`, using an
 * `RpcStreamSender<VideoFrameDto>` to stream typed frame objects.
 */

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { RpcStream } from 'actuallab-rpc';
import { Api, streamingApi,
    type SessionTokenProvider, type StreamServerClient, type VideoFormatDto, type VideoFrameDto } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

/** Session token used by PushVideo — `'~'` = Session.Default, resolved from the
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
    /** Lazily-constructed `IStreamServer` RPC client, bound to the hub's
     *  default peer. */
    rpcStreamServer: StreamServerClient | null;
}

export function serverClockNow(ctx: StreamingContext): number {
    return Date.now() + ctx.serverClockOffsetMs;
}

/**
 * Convert a worker-side `VideoStreamFrame` into the MessagePack map shape
 * expected by `.NET VideoFrame` (`[MessagePackObject(true)]` ⇒ PascalCase keys).
 */
function frameToDto(frame: VideoStreamFrame): VideoFrameDto {
    // Math.trunc coerces to int64-safe integer. @msgpack/msgpack v3 encodes any
    // non-integer number as float64 (0xCB), which the server's ReadInt64 on
    // TimeSpan ticks rejects — tearing down the whole PushVideo stream.
    const dto: VideoFrameDto = {
        Data: frame.data,
        Offset: Math.trunc(frame.offset),
        Duration: Math.trunc(frame.duration),
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
    return dto;
}

/**
 * Lazily initialise the Fusion RPC push peer for the worker context by
 * configuring the shared `Api.hub`'s default peer. The `IStreamServer` client
 * is cached on the `StreamingContext` so every `InternalVideoStream` instance
 * shares the same WebSocket for the life of the worker.
 */
export function ensureRpcPush(ctx: StreamingContext): void {
    if (ctx.rpcStreamServer)
        return;

    if (!ctx.apiUrl)
        throw new Error('Fusion RPC push: apiUrl is not set');

    Api.init('VideoStreaming', {
        url: ctx.apiUrl,
        modules: [streamingApi],
        connectivityUI: WorkerConnectivityUI,
        sessionTokenProvider: ctx.sessionTokenProvider,
    });
    ctx.rpcStreamServer = streamingApi.streamServer;
}

/**
 * Hard cap on the producer-side frame queue. If server ACKs stall (slow link,
 * server overload, peer reconnect), the unbounded Denque used to grow into
 * tens of MB of encoded chunks → V8 major GC pauses → main-thread freeze.
 *
 * 60 frames ≈ 2 s at 30 fps — one full keyframe interval of headroom. Above
 * that we'd rather drop than buffer; live video can't catch up anyway.
 */
const MAX_QUEUE_LENGTH = 60;

export interface InternalVideoStreamCallbacks {
    /** Fired when overflow forced us to drop a keyframe (or run out of
     *  non-keyframes to drop). Listener should ask the encoder to emit a fresh
     *  keyframe so the stream stays decodable downstream. */
    onNeedKeyframe?: () => void;
}

/**
 * In-worker video stream producer. Buffers encoded frames and pumps them to
 * the server via `IStreamServer.PushVideo` over Fusion RPC binary transport.
 */
export class InternalVideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private addedFrameCount = 0;
    private droppedFrameCount = 0;

    public isCompleted = false;
    public isDisposed = false;
    public lastError = '';
    public readonly whenDisposed: Promise<void>;

    constructor(
        private readonly config: {
            codec: string;
            width: number;
            height: number;
            sourceWidth: number;
            sourceHeight: number;
            codecSettings: string;
        },
        private readonly ctx: StreamingContext,
        streamAfter?: Promise<void>,
        private readonly callbacks: InternalVideoStreamCallbacks = {},
    ) {
        this.whenDisposed = this.stream(streamAfter);
    }

    addFrame(frame: VideoStreamFrame): void {
        if (this.isCompleted) return;
        if (frame.data.byteLength === 0) return;

        if (this.frames.length >= MAX_QUEUE_LENGTH) {
            this.dropForOverflow();
        }

        this.frames.push(frame);
        this.addedFrameCount++;

        if (this.addedFrameCount <= 3 || this.addedFrameCount % 300 === 0) {
            debugLog?.log(`addFrame: total: ${this.addedFrameCount} queue: ${this.frames.length} ` +
                `isKey: ${frame.isKeyFrame} size: ${frame.data.byteLength}`);
        }

        this.frameAdded.trigger();
    }

    /**
     * Free a slot when the queue is full. Prefer dropping the oldest
     * non-keyframe (delta frames between keyframes are independently
     * disposable). If only keyframes remain, drop the oldest one and signal
     * the encoder to emit a fresh keyframe — otherwise the stream becomes
     * un-decodable from the next delta forward.
     */
    private dropForOverflow(): void {
        let nonKeyIdx = -1;
        for (let i = 0; i < this.frames.length; i++) {
            if (!this.frames.peekAt(i)!.isKeyFrame) { nonKeyIdx = i; break; }
        }
        if (nonKeyIdx >= 0) {
            this.frames.removeOne(nonKeyIdx);
        } else {
            this.frames.shift();
            this.callbacks.onNeedKeyframe?.();
        }
        this.droppedFrameCount++;
        if (this.droppedFrameCount === 1 || this.droppedFrameCount % 60 === 0) {
            warnLog?.log(`Stream queue overflow: dropped=${this.droppedFrameCount} queue=${this.frames.length}/${MAX_QUEUE_LENGTH}`);
        }
    }

    getAddedFrameCount(): number {
        return this.addedFrameCount;
    }

    getDroppedFrameCount(): number {
        return this.droppedFrameCount;
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
            const streamServer = this.ctx.rpcStreamServer!;
            const peer = Api.peer;

            const clientStartOffset = serverClockNow(this.ctx) / 1000;
            warnLog?.log(`TIMING_ANCHOR: clientStartOffset=${clientStartOffset.toFixed(3)}s`);

            infoLog?.log(`PushVideo: codec=${this.config.codec}, ` +
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
            };

            // Real-time video stream: isRealTime=true, allowReconnect=true, ackPeriod=5, ackAdvance=31.
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
                    isRealTime: true, allowReconnect: true, ackPeriod: 5, ackAdvance: 31,
                    canSkipTo: (frame) => frame.IsKeyFrame,
                },
            );

            // Fire-and-forget: server awaits the frameStream completion. Any
            // rejection is logged but shouldn't cancel the pump loop since the
            // sender owns the lifetime of the stream.
            void streamServer
                .PushVideo(RPC_SESSION_DEFAULT, this.ctx.chatId, clientStartOffset, format, stream.toRef(peer), this.ctx.streamKind)
                .catch((err: unknown) => {
                    const msg = err instanceof Error ? err.message : String(err);
                    warnLog?.log('PushVideo rejected:', err);
                    this.lastError = `PushVideo rejected: ${msg}`;
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
