/**
 * In-worker SignalR video streaming.
 * InternalVideoStream class and SignalR connection management.
 */

import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { HubConnectionState } from '@microsoft/signalr';
import { encode } from '@msgpack/msgpack';
import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

export interface VideoStreamFrame {
    offset: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array;
    codec?: string;
}

export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

export function encodeStreamFrame(frame: VideoStreamFrame): Uint8Array {
    const obj: Record<string, unknown> = {
        offset: frame.offset,
        duration: frame.duration,
        data: frame.data,
    };
    if (frame.isKeyFrame) {
        obj.isKeyFrame = true;
        obj.width = frame.width;
        obj.height = frame.height;
    }
    if (frame.description) obj.description = frame.description;
    if (frame.codec) obj.codec = frame.codec;
    return encode(obj);
}

export interface StreamingContext {
    signalrConnection: signalR.HubConnection | null;
    sessionToken: string;
    chatId: string;
    serverClockOffsetMs: number;
    streamKind: number; // 0 = Webcam, 1 = Screencast
    processing: boolean;
}

export function serverClockNow(ctx: StreamingContext): number {
    return Date.now() + ctx.serverClockOffsetMs;
}

/**
 * Internal VideoStream — simplified version that lives in the worker.
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

            const subject = new signalR.Subject<Uint8Array>();
            const conn = this.ctx.signalrConnection;

            while (conn?.state !== HubConnectionState.Connected && this.ctx.processing) {
                if (conn?.state === HubConnectionState.Disconnected) {
                    try { await conn.start(); } catch { /* retry */ }
                }
                await new Promise(r => setTimeout(r, 100));
            }
            if (!this.ctx.processing) return;

            this.onReconnect?.();

            const clientStartOffset = serverClockNow(this.ctx) / 1000;
            warnLog?.log(`TIMING_ANCHOR: clientStartOffset=${clientStartOffset.toFixed(3)}s`);

            infoLog?.log(`PushVideo: codec=${this.config.codec}, ` +
                `${this.config.width}x${this.config.height}, settings=${this.config.codecSettings.length} chars`);

            void conn!.send('PushVideo',
                this.ctx.sessionToken, this.ctx.chatId,
                this.config.codec, this.config.width, this.config.height,
                this.config.codecSettings, clientStartOffset,
                this.ctx.streamKind, subject);

            while (!this.isCompleted || !this.frames.isEmpty()) {
                while (!this.frames.isEmpty()) {
                    const frame = this.frames.shift()!;
                    const encoded = encodeStreamFrame(frame);
                    subject.next(encoded);
                }
                if (!this.isCompleted) {
                    await this.frameAdded.whenNextVoid();
                }
            }

            subject.complete();
        } catch (error) {
            errorLog?.log('VideoStream error:', error);
        } finally {
            this.isDisposed = true;
        }
    }
}

export function initSignalR(hubUrl: string): signalR.HubConnection {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            transport: signalR.HttpTransportType.WebSockets,
            skipNegotiation: true,
        })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect()
        .build();

    connection.onclose(() => warnLog?.log('SignalR connection closed'));
    connection.onreconnecting(() => warnLog?.log('SignalR reconnecting...'));
    connection.onreconnected(() => infoLog?.log('SignalR reconnected'));

    void connection.start().then(() => {
        infoLog?.log('SignalR connected');
    }).catch((error: unknown) => {
        errorLog?.log('SignalR connection failed:', error);
    });

    return connection;
}
