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
}

export interface VideoStreamFrame {
    offset: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
}

export class VideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();

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

    public addFrame(frame: VideoStreamFrame): void {
        if (this.isCompleted) return;
        if (!frame.data?.length) {
            warnLog?.log('skip empty video frame:', frame);
            return;
        }
        this.frames.push(frame);
        this.frameAdded.trigger();
    }

    public complete(): void {
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        if (this.streamAfter) {
            await this.streamAfter;
        }

        let subject: signalR.Subject<VideoStreamFrame[]> | null = null;
        const chunksToSend = new Array<VideoStreamFrame>();

        while (!this.isDisposed) {
            try {
                if (subject === null || !VideoStreamer.isConnected) {
                    await VideoStreamer.ensureConnected();
                    if (this.isDisposed) return;

                    subject = new signalR.Subject<VideoStreamFrame[]>();
                    // Use PushVideo - simple forwarding, no processing
                    await VideoStreamer.connection.send(
                        'PushVideo',
                        this.sessionToken,
                        this.chatId,
                        this.config.codec,
                        this.config.width,
                        this.config.height,
                        Date.now() / 1000,
                        subject
                    );
                }

                while (VideoStreamer.isConnected && !this.isDisposed) {
                    chunksToSend.length = 0;

                    while (chunksToSend.length < 10) {
                        const frame = this.frames.shift();
                        if (frame) {
                            chunksToSend.push(frame);
                        } else if (this.isCompleted || chunksToSend.length > 0) {
                            break;
                        } else {
                            await this.frameAdded.whenNext();
                        }
                    }

                    if (chunksToSend.length > 0) {
                        subject.next(chunksToSend);
                    }

                    if (this.isCompleted && this.frames.length === 0) {
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
