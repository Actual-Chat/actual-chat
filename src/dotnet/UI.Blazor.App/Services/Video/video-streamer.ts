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
    codecSettings?: string; // Base64 encoded codec-specific data (SPS/PPS for H.264)
}

export interface VideoStreamFrame {
    // offset and duration are in .NET TimeSpan ticks (100-nanosecond units)
    // Convert from microseconds: ticks = microseconds * 10
    offset: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array; // Codec-specific data (SPS/PPS for H.264)
    codec?: string; // Codec identifier (e.g., "avc1" for H.264), only on keyframes
}

// Helper to convert microseconds to .NET TimeSpan ticks
export function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
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
        if (this.isCompleted) {
            console.warn('[VideoStream] addFrame: skipping, stream is completed');
            return;
        }
        if (!frame.data?.length) {
            console.warn('[VideoStream] addFrame: skipping empty frame');
            return;
        }
        this.frames.push(frame);
        // Always log frame additions for debugging
        if (this.frames.length <= 3 || this.frames.length % 30 === 0)
            console.log('[VideoStream] addFrame: added frame, queue size:', this.frames.length, 'isKey:', frame.isKeyFrame, 'size:', frame.data.length);
        this.frameAdded.trigger();
    }

    public complete(): void {
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        console.log('[VideoStream] stream() started, isCompleted:', this.isCompleted, 'isDisposed:', this.isDisposed);

        if (this.streamAfter) {
            debugLog?.log('[VideoStream] Waiting for previous stream to complete...');
            await this.streamAfter;
            debugLog?.log('[VideoStream] Previous stream completed');
        }

        let subject: signalR.Subject<VideoStreamFrame[]> | null = null;
        const chunksToSend = new Array<VideoStreamFrame>();

        while (!this.isDisposed) {
            try {
                if (subject === null || !VideoStreamer.isConnected) {
                    debugLog?.log('[VideoStream] Connecting to SignalR...');
                    await VideoStreamer.ensureConnected();
                    if (this.isDisposed) {
                        debugLog?.log('[VideoStream] Disposed while connecting, exiting');
                        return;
                    }

                    console.log('[VideoStream] Connected, creating subject and calling PushVideo with codecSettings:', this.config.codecSettings?.length ?? 0, 'chars');
                    subject = new signalR.Subject<VideoStreamFrame[]>();
                    // Use PushVideo - simple forwarding, no processing
                    await VideoStreamer.connection.send(
                        'PushVideo',
                        this.sessionToken,
                        this.chatId,
                        this.config.codec,
                        this.config.width,
                        this.config.height,
                        this.config.codecSettings ?? '', // Base64 encoded SPS/PPS for H.264
                        Date.now() / 1000,
                        subject
                    );
                    console.log('[VideoStream] PushVideo called successfully with codecSettings');
                }

                while (VideoStreamer.isConnected && !this.isDisposed) {
                    chunksToSend.length = 0;

                    while (chunksToSend.length < 10) {
                        const frame = this.frames.shift();
                        if (frame) {
                            chunksToSend.push(frame);
                        } else if (this.isCompleted || chunksToSend.length > 0) {
                            debugLog?.log('[VideoStream] Breaking inner loop: isCompleted=', this.isCompleted, 'chunksToSend.length=', chunksToSend.length);
                            break;
                        } else {
                            // debugLog?.log('[VideoStream] Waiting for frames...');
                            await this.frameAdded.whenNext();
                        }
                    }

                    if (chunksToSend.length > 0) {
                        console.log('[VideoStream] Sending', chunksToSend.length, 'frames to server');
                        // Send a copy of the array to avoid race condition with clearing
                        subject.next([...chunksToSend]);
                    }

                    if (this.isCompleted && this.frames.length === 0) {
                        console.log('[VideoStream] Stream completed, calling subject.complete()');
                        subject.complete();
                        this.isDisposed = true;
                    }
                }
                debugLog?.log('[VideoStream] Exited inner while loop, isConnected:', VideoStreamer.isConnected, 'isDisposed:', this.isDisposed);
            } catch (error) {
                subject = null;
                errorLog?.log('[VideoStream] stream error:', error);
            }
        }
        debugLog?.log('[VideoStream] stream() exiting');
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
