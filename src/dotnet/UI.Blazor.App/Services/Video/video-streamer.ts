import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { HubConnectionState } from '@microsoft/signalr';
import { encode } from '@msgpack/msgpack';
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

/**
 * Encode a VideoStreamFrame as a MessagePack byte array.
 * This matches the server-side VideoFrameDto MessagePack format
 * with string keys (camelCase).
 */
function encodeFrame(frame: VideoStreamFrame): Uint8Array {
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
    if (frame.description) {
        obj.description = frame.description;
    }
    if (frame.codec) {
        obj.codec = frame.codec;
    }
    return encode(obj);
}

export class VideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private addedFrameCount = 0;

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
            warnLog?.log('addFrame: skipping, stream is completed');
            return;
        }
        if (!frame.data.length) {
            warnLog?.log('addFrame: skipping empty frame');
            return;
        }
        this.frames.push(frame);
        this.addedFrameCount++;
        if (this.addedFrameCount <= 3 || this.addedFrameCount % 300 === 0)
            debugLog?.log('addFrame: total:', this.addedFrameCount, 'queue:', this.frames.length, 'isKey:', frame.isKeyFrame, 'size:', frame.data.length);
        this.frameAdded.trigger();
    }

    public complete(): void {
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        infoLog?.log('stream() started');

        if (this.streamAfter) {
            debugLog?.log('Waiting for previous stream to complete...');
            await this.streamAfter;
            debugLog?.log('Previous stream completed');
        }

        // Subject sends byte[][] (each frame MessagePack-encoded as byte[])
        // to match server-side PushVideo(... IAsyncEnumerable<byte[][]>)
        let subject: signalR.Subject<Uint8Array[]> | null = null;
        const chunksToSend = new Array<VideoStreamFrame>();

        while (!this.isDisposed) {
            try {
                if (subject === null || !VideoStreamer.isConnected) {
                    debugLog?.log('Connecting to SignalR...');
                    await VideoStreamer.ensureConnected();

                    infoLog?.log('Connected, calling PushVideo with codecSettings:', this.config.codecSettings?.length ?? 0, 'chars');
                    subject = new signalR.Subject<Uint8Array[]>();
                    // Use PushVideo - simple forwarding, no processing
                    await VideoStreamer.connection!.send(
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
                    infoLog?.log('PushVideo called successfully');
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
                            // debugLog?.log('Waiting for frames...');
                            await this.frameAdded.whenNext();
                        }
                    }

                    if (chunksToSend.length > 0) {
                        // Encode each frame as MessagePack bytes, send as byte[][]
                        const encodedFrames = chunksToSend.map(f => encodeFrame(f));
                        subject.next(encodedFrames);
                    }

                    if (this.isCompleted && this.frames.length === 0) {
                        infoLog?.log('Stream completed');
                        subject.complete();
                        this.isDisposed = true;
                    }
                }
                debugLog?.log('Exited inner while loop, isConnected:', VideoStreamer.isConnected, 'isDisposed:', this.isDisposed);
            } catch (error) {
                subject = null;
                errorLog?.log('stream error:', error);
            }
        }
        debugLog?.log('stream() exiting');
    }
}

export class VideoStreamer {
    public static connection: signalR.HubConnection | null = null;
    public static readonly streams = new Array<VideoStream>();
    public static lastStream: VideoStream | null = null;

    public static init(hubUrl: string): void {
        infoLog?.log('init called with hubUrl:', hubUrl);
        if (this.connection) {
            warnLog?.log('Connection already exists, state:', this.connection.state);
            return;
        }

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets,
            })
            .withAutomaticReconnect()
            .withHubProtocol(new MessagePackHubProtocol())
            .build();

        this.connection.onclose((err) => warnLog?.log('Connection closed:', err));
        this.connection.onreconnecting((err) => warnLog?.log('Reconnecting:', err));
        this.connection.onreconnected((id) => warnLog?.log('Reconnected:', id));

        this.connection.start()
            .then(() => infoLog?.log('Connected successfully, state:', this.connection!.state))
            .catch((err: unknown) => errorLog?.log('Connection failed:', err));
    }

    public static get isConnected(): boolean {
        return this.connection?.state === HubConnectionState.Connected;
    }

    public static async ensureConnected(): Promise<void> {
        while (!this.isConnected) {
            if (this.connection!.state === HubConnectionState.Disconnected) {
                await this.connection!.start();
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
