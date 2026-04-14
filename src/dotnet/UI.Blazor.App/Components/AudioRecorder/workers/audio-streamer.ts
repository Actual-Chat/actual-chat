/* eslint-disable */
import { AUDIO_STREAMER as AS, AUDIO_ENCODER as AE } from '_constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { EventHandlerSet } from 'event-handling';
import { ObjectPool } from 'object-pool';
import { delayAsync } from 'promises';
import { RpcHub, RpcClientPeer, RpcClientStreamSender } from 'actuallab-rpc';
import { StreamServerDef, type AudioFrameDto } from '../../../Services/Video/streaming-rpc-service';
import { ServerClock } from 'server-clock';
import { WorkerConnectivityUI } from './worker-connectivity-ui';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog } = Log.get('AudioStreamer');
const bufferPool: ObjectPool<ArrayBufferLike> = new ObjectPool<ArrayBufferLike>(
    () => new ArrayBuffer(AE.FRAME_BUFFER_BYTES)
).expandTo(20);

/** Serialization format for Fusion RPC. */
const RPC_SERIALIZATION_FORMAT = 'msgpack6';

/** 20ms in .NET TimeSpan ticks (100ns units). */
const FRAME_DURATION_TICKS = AE.FRAME_DURATION_MS * 10_000;

/** Session.Default — resolved from the WebSocket connection context. */
const RPC_SESSION_DEFAULT = '~';

export class AudioStream implements Disposable {
    public static totalCount = 0;

    private readonly frames = new Denque<Uint8Array>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private firstFrameTimestamp: number | null = null;

    public readonly name: string;
    public isCompleted = false;
    public isDisposed = false;
    public readonly whenDisposed: Promise<void>;

    constructor(
        private readonly preSkip: number,
        private readonly chatId: string,
        private repliedChatEntryId?: string,
        private streamAfter?: Promise<void>,
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
        if (this.isCompleted)
            return;

        debugLog?.log(`${this.name}.complete`);
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    public addFrame(source: Uint8Array | EncodedAudioChunk, isEncodedAudioChunk = false): void {
        if (!source || source.byteLength == 0 || this.isCompleted)
            return;

        this.firstFrameTimestamp ??= ServerClock.now();

        const buffer = bufferPool.get();
        let frame: Uint8Array;
        if (source.byteLength <= buffer.byteLength)
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
        while (this.frames.length > AS.MAX_BUFFERED_FRAMES) {
            const oldFrame = this.frames.shift()!;
            if (oldFrame.buffer.byteLength === AE.FRAME_BUFFER_BYTES)
                bufferPool.release(oldFrame.buffer);
        }
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        if (this.streamAfter != null) {
            await this.streamAfter;
            this.streamAfter = undefined;
        }

        // Wait for initial frames before starting the stream
        while (!this.isCompleted && this.frames.length <= AS.DELAY_FRAMES)
            await this.frameAdded.whenNext();

        if (this.isDisposed)
            return;
        if (this.isCompleted && this.frames.length === 0)
            return;

        let sender: RpcClientStreamSender<AudioFrameDto> | null = null;
        try {
            await AudioStreamer.ensureConnected();
            if (this.isDisposed)
                return;
            if (!AudioStreamer.rpcPeer || !AudioStreamer.streamServerClient) {
                warnLog?.log(`${this.name}: peer not connected, skipping stream`);
                return;
            }

            const streamServer = AudioStreamer.streamServerClient;
            const peer = AudioStreamer.rpcPeer;

            const clientStartOffset = (this.firstFrameTimestamp ?? ServerClock.now()) / 1000;
            infoLog?.log(`${this.name}: PushAudio clientStartOffset=${clientStartOffset.toFixed(3)}s`);

            sender = new RpcClientStreamSender<AudioFrameDto>(peer);

            void streamServer
                .PushAudio(RPC_SESSION_DEFAULT, this.chatId, this.repliedChatEntryId ?? null,
                    clientStartOffset, this.preSkip, sender.toRef())
                .catch((err: unknown) => warnLog?.log(`${this.name}: PushAudio rejected:`, err));

            this.repliedChatEntryId = undefined;

            await sender.whenStarted();
            warnLog?.log(`${this.name}: sender started, peerConnected=${AudioStreamer.rpcPeer?.isConnected}`);

            let frameIndex = 0;
            while (!this.isDisposed) {
                // Detect server disconnect (e.g. pod restart)
                if (sender.isEnded) {
                    warnLog?.log(`${this.name}: sender ended (disconnectedByServer=${sender.isDisconnectedByServer}), ${frameIndex} frames sent`);
                    break;
                }

                const frame = this.frames.shift();
                if (frame) {
                    sender.sendItem({
                        Data: frame,
                        Offset: frameIndex * FRAME_DURATION_TICKS,
                        Duration: FRAME_DURATION_TICKS,
                        IsKeyFrame: true,
                    });
                    frameIndex++;

                    // Release buffer back to pool
                    if (frame.buffer.byteLength === AE.FRAME_BUFFER_BYTES)
                        bufferPool.release(frame.buffer);
                } else if (this.isCompleted && this.frames.length === 0) {
                    warnLog?.log(`${this.name}: stream completed, ${frameIndex} frames sent`);
                    sender.sendEnd();
                    this.dispose();
                } else {
                    await this.frameAdded.whenNext();
                }
            }
        } catch (error) {
            warnLog?.log(`${this.name}: stream error:`, error);
            try { sender?.sendEnd(error instanceof Error ? error : new Error(String(error))); }
            catch { /* ignore */ }
        } finally {
            this.dispose();
        }
    }
}

export class AudioStreamer {
    public static rpcHub: RpcHub | null = null;
    public static rpcPeer: RpcClientPeer | null = null;
    public static streamServerClient: {
        PushAudio(session: string, chatId: string, repliedChatEntryId: string | null,
            clientStartOffset: number, preSkip: number, frameStreamRef: unknown): Promise<void>;
    } | null = null;
    public static readonly streams = new Array<AudioStream>();
    public static lastStream: AudioStream | null = null;
    public static connectionStateChangedEvents = new EventHandlerSet<boolean>()

    public static init(rpcWsUrl: string): void {
        if (this.isInitialized)
            return;

        debugLog?.log(`init`, rpcWsUrl);

        this.rpcHub = new RpcHub();
        this.rpcPeer = new RpcClientPeer(this.rpcHub, rpcWsUrl, RPC_SERIALIZATION_FORMAT);
        void this.rpcPeer.run();
        this.streamServerClient = this.rpcHub.addClient(this.rpcPeer, StreamServerDef) as unknown as typeof this.streamServerClient;
    }

    public static get isInitialized(): boolean {
        return this.rpcPeer != null;
    }

    public static get isConnected(): boolean {
        // RPC peer is considered connected once initialized — the RPC framework
        // handles reconnection transparently.
        return this.rpcPeer != null;
    }

    public static async disconnect(): Promise<void> {
        infoLog?.log(`disconnect`);
        this.rpcPeer?.close();
        this.rpcPeer = null;
        this.streamServerClient = null;
        this.rpcHub = null;
        updateConnectionState(false);
    }

    public static async ensureConnected(): Promise<void> {
        if (this.isConnected)
            return;

        // Wait for online if offline
        if (!WorkerConnectivityUI.isOnline) {
            warnLog?.log('ensureConnected: offline, waiting for online...');
            await new Promise<void>(resolve => {
                WorkerConnectivityUI.isOnlineChanged.addJustOnce((isOnline) => {
                    if (isOnline)
                        resolve();
                });
            });
        }
    }

    public static addStream(sessionToken: string, preSkip: number, chatId: string, repliedChatEntryId: string): AudioStream {
        let stream: AudioStream;
        if (this.streams.length < AS.MAX_STREAMS) {
            stream = new AudioStream(preSkip, chatId, repliedChatEntryId, this.lastStream?.whenDisposed);
            this.lastStream = stream;
            this.streams.push(stream)
        }
        else {
            // Fake stream that won't stream anything
            stream = new AudioStream(preSkip, chatId, repliedChatEntryId, delayAsync(100));
            stream.dispose()
        }
        return stream;
    }
}

function updateConnectionState(isConnected: boolean): void {
    infoLog?.log(`isConnected:`, isConnected);
    AudioStreamer.connectionStateChangedEvents.trigger(isConnected);
}
