import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { HubConnectionState } from '@microsoft/signalr';
import Denque from 'denque';
import { EventHandlerSet } from 'event-handling';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoStreamer');

export interface VideoStreamConfig {
    codec: string;
    width: number;
    height: number;
    audioStreamId?: string;
}

export class VideoStream {
    private readonly chunks = new Denque<Uint8Array>();
    private readonly chunkAdded = new EventHandlerSet<void>();

    public isCompleted = false;
    public isDisposed = false;
    public readonly whenDisposed: Promise<void>;

    constructor(
        private readonly sessionToken: string,
        private readonly chatId: string,
        private readonly config: VideoStreamConfig,
        private streamAfter?: Promise<void>,
    ) {
        this.whenDisposed = this.stream();
    }

    public addChunk(chunk: Uint8Array): void {
        if (this.isCompleted) return;
        this.chunks.push(chunk);
        this.chunkAdded.trigger();
    }

    public complete(): void {
        this.isCompleted = true;
        this.chunkAdded.trigger();
    }

    private async stream(): Promise<void> {
        if (this.streamAfter) {
            await this.streamAfter;
        }

        let subject: signalR.Subject<Array<Uint8Array>> | null = null;
        const chunksToSend = new Array<Uint8Array>();

        while (!this.isDisposed) {
            try {
                if (subject === null || !VideoStreamer.isConnected) {
                    await VideoStreamer.ensureConnected();
                    if (this.isDisposed) return;

                    subject = new signalR.Subject<Array<Uint8Array>>();
                    // Use PushVideo - simple forwarding, no processing
                    await VideoStreamer.connection.send(
                        'PushVideo',
                        this.sessionToken,
                        this.chatId,
                        this.config.codec,
                        this.config.width,
                        this.config.height,
                        Date.now() / 1000,
                        this.config.audioStreamId,
                        subject
                    );
                }

                while (VideoStreamer.isConnected && !this.isDisposed) {
                    chunksToSend.length = 0;

                    while (chunksToSend.length < 10) {
                        const chunk = this.chunks.shift();
                        if (chunk) {
                            chunksToSend.push(chunk);
                        } else if (this.isCompleted || chunksToSend.length > 0) {
                            break;
                        } else {
                            await this.chunkAdded.whenNext();
                        }
                    }

                    if (chunksToSend.length > 0) {
                        subject.next(chunksToSend);
                    }

                    if (this.isCompleted && this.chunks.length === 0) {
                        subject.complete();
                        this.isDisposed = true;
                    }
                }
            } catch (error) {
                subject = null;
                warnLog?.log('stream error:', error);
            }
        }
    }
}

export class VideoStreamer {
    public static connection: signalR.HubConnection;
    public static readonly streams = new Array<VideoStream>();
    public static lastStream: VideoStream | null = null;

    public static init(hubUrl: string): void {
        if (this.connection) return;

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets,
            })
            .withAutomaticReconnect()
            .withHubProtocol(new MessagePackHubProtocol())
            .build();

        this.connection.start();
    }

    public static get isConnected(): boolean {
        return this.connection?.state === HubConnectionState.Connected;
    }

    public static async ensureConnected(): Promise<void> {
        while (!this.isConnected) {
            if (this.connection.state === HubConnectionState.Disconnected) {
                await this.connection.start();
            }
            await new Promise(r => setTimeout(r, 100));
        }
    }

    public static addStream(
        sessionToken: string,
        chatId: string,
        config: VideoStreamConfig
    ): VideoStream {
        const stream = new VideoStream(
            sessionToken,
            chatId,
            config,
            this.lastStream?.whenDisposed
        );
        this.lastStream = stream;
        this.streams.push(stream);
        return stream;
    }
}
