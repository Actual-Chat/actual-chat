/**
 * In-worker video streaming over Fusion RPC binary transport.
 *
 * Sends encoded frames to the server via `IStreamServer.PushVideo`, using an
 * `RpcStreamSender<VideoFrameDto>` to stream typed frame objects. The
 * server keeps its SignalR `StreamHub.PushVideo` endpoint for backward
 * compatibility with older clients, but this worker no longer touches it.
 */

import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { Log } from 'logging';
import { RpcHub, RpcClientPeer, RpcStream } from 'actuallab-rpc';
import {
    StreamServerDef,
    type VideoFormatDto,
    type VideoFrameDto,
} from '../streaming-rpc-service';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

/** Serialization format for Fusion RPC push. Matches the pull side. */
const RPC_SERIALIZATION_FORMAT = 'msgpack6';

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
}

export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

export interface StreamingContext {
    sessionToken: string;
    chatId: string;
    serverClockOffsetMs: number;
    streamKind: number; // 0 = Webcam, 1 = Screencast
    processing: boolean;
    /** Fusion RPC WebSocket URL. */
    rpcWsUrl: string | null;
    /** Lazily-constructed RPC hub (worker-local). */
    rpcHub: RpcHub | null;
    /** Lazily-constructed RPC client peer (worker-local). */
    rpcPeer: RpcClientPeer | null;
    /** Lazily-constructed `IStreamServer` RPC client. */
    rpcStreamServer: {
        PushVideo(session: string, chatId: string, clientStartOffset: number,
            format: VideoFormatDto, frameStreamRef: unknown): Promise<void>;
    } | null;
}

export function serverClockNow(ctx: StreamingContext): number {
    return Date.now() + ctx.serverClockOffsetMs;
}

/**
 * Convert a worker-side `VideoStreamFrame` into the MessagePack map shape
 * expected by `.NET VideoFrame` (`[MessagePackObject(true)]` ⇒ PascalCase keys).
 */
function frameToDto(frame: VideoStreamFrame): VideoFrameDto {
    const dto: VideoFrameDto = {
        Data: frame.data,
        Offset: frame.offset,
        Duration: frame.duration,
        IsKeyFrame: frame.isKeyFrame,
    };
    if (frame.isKeyFrame) {
        dto.Width = frame.width;
        dto.Height = frame.height;
    }
    if (frame.description) dto.Description = frame.description;
    if (frame.codec) dto.Codec = frame.codec;
    if (frame.temporalLayerId !== undefined && frame.temporalLayerId > 0)
        dto.TemporalLayerId = frame.temporalLayerId;
    return dto;
}

/**
 * Lazily initialise the Fusion RPC push peer for the worker context. The hub,
 * peer and `IStreamServer` client are cached on the `StreamingContext` so every
 * `InternalVideoStream` instance shares the same WebSocket for the life of the
 * worker.
 */
export function ensureRpcPush(ctx: StreamingContext): void {
    if (ctx.rpcPeer && ctx.rpcStreamServer) return;
    if (!ctx.rpcWsUrl)
        throw new Error('Fusion RPC push: rpcWsUrl is not set');
    ctx.rpcHub ??= new RpcHub(); // hubId must be a UUID; RpcHub() assigns one
    if (!ctx.rpcPeer) {
        ctx.rpcPeer = new RpcClientPeer(ctx.rpcHub, ctx.rpcWsUrl, RPC_SERIALIZATION_FORMAT);
        void ctx.rpcPeer.run();
    }
    ctx.rpcStreamServer ??= ctx.rpcHub.addClient(ctx.rpcPeer, StreamServerDef) as unknown as {
        PushVideo(session: string, chatId: string, clientStartOffset: number,
            format: VideoFormatDto, frameStreamRef: unknown): Promise<void>;
    };
}

/**
 * In-worker video stream producer. Buffers encoded frames and pumps them to
 * the server via `IStreamServer.PushVideo` over Fusion RPC binary transport.
 */
export class InternalVideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private addedFrameCount = 0;

    public isCompleted = false;
    public isDisposed = false;
    public readonly whenDisposed: Promise<void>;

    constructor(
        private readonly config: { codec: string; width: number; height: number; codecSettings: string },
        private readonly ctx: StreamingContext,
        private readonly onReconnect?: () => void,
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
            const peer = this.ctx.rpcPeer!;

            this.onReconnect?.();

            const clientStartOffset = serverClockNow(this.ctx) / 1000;
            warnLog?.log(`TIMING_ANCHOR: clientStartOffset=${clientStartOffset.toFixed(3)}s`);

            infoLog?.log(`PushVideo: codec=${this.config.codec}, ` +
                `${this.config.width}x${this.config.height}, settings=${this.config.codecSettings.length} chars`);

            const format: VideoFormatDto = {
                Codec: this.config.codec,
                Width: this.config.width,
                Height: this.config.height,
                CodecSettings: this.config.codecSettings,
            };

            // Real-time video stream: isRealTime=true, allowReconnect=false, ackPeriod=5, ackAdvance=31.
            // toRef() creates the sender, registers it, and starts pumping in the background.
            // eslint-disable-next-line @typescript-eslint/no-this-alias -- async generator function* can't be arrow
            const self = this;
            const stream = new RpcStream<VideoFrameDto>((async function* () {
                while (!self.isCompleted || !self.frames.isEmpty()) {
                    while (!self.frames.isEmpty()) {
                        yield frameToDto(self.frames.shift()!);
                    }
                    if (!self.isCompleted) {
                        await self.frameAdded.whenNextVoid();
                    }
                }
            })(), {
                isRealTime: true, allowReconnect: false, ackPeriod: 5, ackAdvance: 31,
                canSkipTo: (frame) => frame.IsKeyFrame,
            });

            // Fire-and-forget: server awaits the frameStream completion. Any
            // rejection is logged but shouldn't cancel the pump loop since the
            // sender owns the lifetime of the stream.
            void streamServer
                .PushVideo(RPC_SESSION_DEFAULT, this.ctx.chatId, clientStartOffset, format, stream.toRef(peer))
                .catch((err: unknown) => warnLog?.log('PushVideo rejected:', err));

            // Wait for the pump to complete (writeFrom was started by toRef)
            await stream.whenSent;
        } catch (error) {
            errorLog?.log('VideoStream error:', error);
        } finally {
            this.isDisposed = true;
        }
    }
}
