import { AUDIO_STREAMER as AS, AUDIO_ENCODER as AE } from '_constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { EventHandlerSet } from 'event-handling';
import { ObjectPool } from 'object-pool';
import { delayAsync } from 'promises';
import * as signalR from '@microsoft/signalr';
import { HubConnectionState, IStreamResult } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('AudioStreamer');
const bufferPool: ObjectPool<ArrayBuffer> = new ObjectPool<ArrayBuffer>(
    () => new ArrayBuffer(AE.FRAME_BYTES * 2)
).expandTo(20);

export class AudioStream implements Disposable {
    public static totalCount = 0;

    private readonly frames = new Denque<Uint8Array>();
    private readonly frameAdded = new EventHandlerSet<void>();

    public readonly name: string;
    public isCompleted = false;
    public isDisposed = false;
    public readonly whenDisposed: Promise<void> = null;

    constructor(
        private readonly sessionToken: string,
        private readonly preSkip: number,
        private readonly chatId: string,
        private repliedChatEntryId: string,
        private streamAfter: Promise<void> = null,
    ) {
        this.name = `AudioStream(${chatId}).${AudioStream.totalCount++}`
        this.whenDisposed = (async () => {
            try {
                await this.stream();
            } catch { }
            await delayAsync(AS.INTER_STREAM_DELAY * 1000);
        })();
    }

    public dispose() {
        // dispose = "stop streaming as quickly as possible"
        if (this.isDisposed)
            return;

        debugLog?.log(`${this.name}.dispose`);
        this.isDisposed = true;
        this.complete();
        const index = AudioStreamer.streams.indexOf(this)
        if (index >= 0)
            AudioStreamer.streams.splice(index, 1);
    }

    public complete(): void {
        // complete = "send everything and dispose"
        if (this.isCompleted)
            return;

        debugLog?.log(`${this.name}.complete`);
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    public addFrame(source: Uint8Array | EncodedAudioChunk, isEncodedAudioChunk = false): void {
        if (!source || source.byteLength == 0 || this.isCompleted)
            return;

        const buffer = bufferPool.get();
        let frame : Uint8Array;
        if (buffer.byteLength >= source.byteLength)
            frame = new Uint8Array(buffer, 0, source.byteLength)
        else {
            frame = new Uint8Array(source.byteLength);
            bufferPool.release(buffer);
        }
        if (isEncodedAudioChunk)
            (source as EncodedVideoChunk).copyTo(frame);
        else
            frame.set(source as Uint8Array, 0);

        this.frames.push(frame);
        while (this.frames.length > AS.MAX_BUFFERED_FRAMES)
            this.frames.shift();
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        if (this.streamAfter != null) {
            // We want audio streams to go in order, otherwise the streams buffered
            // while the client was disconnected may come out of order, and thus create
            // out-of-order messages.
            await this.streamAfter;
            this.streamAfter = null;
        }

        while (!this.isCompleted && this.frames.length <= AS.DELAY_FRAMES)
            await this.frameAdded.whenNext();

        let subject: signalR.Subject<Array<Uint8Array>> = null;
        const framesToSend = new Array<Uint8Array>();
        while (!this.isDisposed) {
            try {
                if (subject === null || !AudioStreamer.isConnected) {
                    await AudioStreamer.ensureConnected();
                    if (this.isDisposed)
                        return;
                    if (this.isCompleted && this.frames.length === 0)
                        return; // Same as disposed, we don't want to send an empty stream in this case

                    subject = new signalR.Subject<Array<Uint8Array>>();
                    await AudioStreamer.connection.send(
                        'ProcessAudioChunks',
                        this.sessionToken, this.chatId, this.repliedChatEntryId,
                        Date.now() / 1000, this.preSkip, subject);
                    this.repliedChatEntryId = null; // We don't want to send a few "replies" in case we retry
                }
                while (AudioStreamer.isConnected && !this.isDisposed) {
                    // Prepare framesToSend
                    framesToSend.length = 0;
                    while (framesToSend.length < AS.MAX_PACK_FRAMES) {
                        const frame = this.frames.shift()
                        if (frame !== undefined) {
                            framesToSend.push(frame);
                            continue;
                        }
                        if (this.isCompleted || framesToSend.length >= AS.MIN_PACK_FRAMES)
                            break;

                        await this.frameAdded.whenNext();
                    }

                    // Send framesToSend
                    try {
                        if (framesToSend.length !== 0) {
                            debugLog?.log(`${this.name}.stream: sending ${framesToSend.length} frame(s)`);
                            subject.next(framesToSend);
                            // We don't want to send frames too quickly, otherwise streams may overlap
                            await delayAsync(framesToSend.length * AE.FRAME_DURATION_MS / AS.MAX_SPEED);
                        }
                    }
                    finally {
                        if (framesToSend.length !== 0) {
                            framesToSend.forEach(f => bufferPool.release(f.buffer))
                            framesToSend.length = 0;
                        }
                    }

                    // Try complete streaming
                    if (this.isCompleted && this.frames.length === 0) {
                        debugLog?.log(`${this.name}.stream: completing`);
                        subject.complete();
                        this.dispose(); // No-op if already disposed
                    }
                }
            }
            catch (error) {
                subject = null; // This triggers retry sending
                warnLog?.log(`stream: error:`, error);
                delayAsync(AS.STREAM_ERROR_DELAY * 1000);
            }
        }
    }
}

export class AudioStreamer {
    public static connection: signalR.HubConnection = null;
    public static readonly streams = new Array<AudioStream>();
    public static lastStream: AudioStream | null = null;
    public static connectionStateChangedEvents = new EventHandlerSet<boolean>()

    public static init(hubUrl: string): void {
        if (this.isInitialized)
            return;

        debugLog?.log(`init`, hubUrl);
        debugBeginRandomDisconnects(AS.DEBUG.RANDOM_DISCONNECT_PERIOD_MS);

        const c = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets,
            })
            // We use fixed number of attempts here, because the reconnection is anyway
            // triggered after SSB / Stl.Rpc reconnect. See:
            // - C#: ChatAudioUI.ReconnectOnRpcReconnect
            // - TS: AudioRecorder.ctor.
            // Some extra attempts are needed, coz there is a chance that the primary connection
            // stays intact, while this one drops somehow.
            .withAutomaticReconnect([50, 350, 500, 1000, 1000, 1000])
            .withHubProtocol(new MessagePackHubProtocol())
            .configureLogging(signalR.LogLevel.Information)
            .build();
        // stateful reconnect doesn't work with skipNegotiation and moreover provides glitches
        c.onreconnected(() => updateConnectionState());
        c.onreconnecting(() => updateConnectionState());
        c.onclose(() => updateConnectionState());
        c['_launchStreams'] = _launchStreams.bind(c);
        c.start();
        this.connection = c;
    }

    public static get isInitialized(): boolean {
        return this.connection !== null;
    }

    public static get isConnected(): boolean {
        updateConnectionState();
        return lastIsConnected;
    }

    public static async disconnect(): Promise<void> {
        infoLog?.log(`disconnect:`, this.connection.state);
        if (this.connection.state !== 'Disconnected')
            await this.connection.stop();
    }

    public static async ensureConnected(quickReconnect = false): Promise<void> {
        if (this.isConnected)
            return;

        const c = this.connection;
        infoLog?.log(`ensureConnected(${quickReconnect}): connection.state:`, c.state);
        while (!this.isConnected) {
            try {
                if (c.state === HubConnectionState.Disconnecting)
                    await c.stop();
                if (c.state === HubConnectionState.Disconnected) {
                    await c.start();
                    continue;
                }

                // c.State === HubConnectionState.Connecting or Reconnecting
                const maxConnectDuration = quickReconnect
                    ? AS.MAX_QUICK_CONNECT_DURATION
                    : AS.MAX_CONNECT_DURATION;
                quickReconnect = false; // We use MAX_QUICK_CONNECT_DURATION just once
                for (let t = 0; t < maxConnectDuration; t += 0.1) {
                    await delayAsync(100);
                    if (this.isConnected)
                        return;
                }

                // And if the connection wasn't established, we reconnect
                await c.stop();
                await c.start();
            }
            catch (error) {
                warnLog?.log(`ensureConnected: error:`, error);
                delayAsync(AS.CONNECT_ERROR_DELAY * 1000);
            }
        }
    }

    public static addStream(sessionToken: string, preSkip: number, chatId: string, repliedChatEntryId: string): AudioStream {
        let stream: AudioStream;
        if (this.streams.length < AS.MAX_STREAMS) {
            stream = new AudioStream(sessionToken, preSkip, chatId, repliedChatEntryId, this.lastStream?.whenDisposed);
            this.lastStream = stream;
            this.streams.push(stream)
        }
        else {
            // Fake stream that won't stream anything
            stream = new AudioStream(sessionToken, preSkip, chatId, repliedChatEntryId, delayAsync(100));
            stream.dispose()
        }
        return stream;
    }
}

let lastIsConnected = false;
function updateConnectionState(): void {
    const isConnected = AudioStreamer.connection?.state === HubConnectionState.Connected;
    if (lastIsConnected === isConnected)
        return;

    lastIsConnected = isConnected;
    infoLog?.log(`isConnected:`, isConnected);
    AudioStreamer.connectionStateChangedEvents.trigger(isConnected);
}

// Override HubConnection._launchStreams
function _launchStreams(streams: IStreamResult<any>[], promiseQueue: Promise<void>): void {
    if (streams.length === 0)
        return;

    // Synchronize stream data so they arrive in-order on the server
    if (!promiseQueue)
        promiseQueue = Promise.resolve();

    // We want to iterate over the keys, since the keys are the stream ids
    // eslint-disable-next-line guard-for-in
    for (const streamId in streams) {
        streams[streamId].subscribe({
            complete: () => {
                promiseQueue = promiseQueue.then(() => this._sendWithProtocol(this._createCompletionMessage(streamId)));
            },
            error: (err) => {
                let message: string;
                if (err instanceof Error) {
                    message = err.message;
                } else if (err && err.toString) {
                    message = err.toString();
                } else {
                    message = "Unknown error";
                }

                const protocolMessage = this._protocol.writeMessage(this._createCompletionMessage(streamId, message));
                promiseQueue = promiseQueue.then(() => this._sendMessage(protocolMessage));
            },
            next: (item) => {
                const protocolMessage = this._protocol.writeMessage(this._createStreamItemMessage(streamId, item));
                promiseQueue = promiseQueue.then(() => this._sendMessage(protocolMessage));
            },
        });
    }
}

function debugBeginRandomDisconnects(periodMs: number) {
    if (!periodMs || periodMs <= 0)
        return;

    const originalSend = WebSocket.prototype.send;
    const sockets: WebSocket[] = [];

    WebSocket.prototype.send = function(...args) {
        if (sockets.indexOf(this) === -1)
            sockets.push(this);
        return originalSend.call(this, ...args);
    };

    (async () => {
        // noinspection InfiniteLoopJS
        while (true) {
            await delayAsync(periodMs * (1 + Math.random()) / 2);
            if (sockets.length == 0)
                continue;

            warnLog.log(`Killing ${sockets.length} WebSocket connection(s)...`);
            try {
                sockets.forEach(x => x.close(3666, 'KILLED!')); // 3666 is just an error code
            }
            catch { }
            sockets.length = 0;
        }
    })();
}
