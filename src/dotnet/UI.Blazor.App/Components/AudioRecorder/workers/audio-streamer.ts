/* eslint-disable */
import { AUDIO } from 'app-constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { EventHandlerSet } from 'event-handling';
import { ObjectPool } from 'object-pool';
import { delayAsync } from 'actuallab-core';
import { RpcClientPeer, RpcConnectionState, RpcStream } from 'actuallab-rpc';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment,
    type ApiModule, type AudioFrameDto, type SessionTokenProvider } from 'api';
import { ServerClock } from 'clocks';
import { WorkerConnectivityUI } from './worker-connectivity-ui';
import { getLogs } from 'logging';

const { debugLog, infoLog, warnLog } = getLogs('AudioStreamer');
// bufferPool depends on AUDIO and is initialized lazily in
// AudioStreamer.init (which runs after initAppConstants).
let bufferPool: ObjectPool<ArrayBufferLike> = null!;

/** Session.Default — resolved from the WebSocket connection context. */
const RPC_SESSION_DEFAULT = '~';

interface AudioStreamFrame {
    data: Uint8Array;
}

export class AudioStream implements Disposable {
    public static totalCount = 0;

    private readonly frames = new Denque<AudioStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private sourceStartedAtMs: number | null = null;

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

    public addFrame(source: Uint8Array | EncodedAudioChunk, isEncodedAudioChunk = false, sourceCapturedAtMs?: number): void {
        if (!source || source.byteLength == 0 || this.isCompleted)
            return;

        this.sourceStartedAtMs ??= sourceCapturedAtMs ?? ServerClock.now();

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

        this.frames.push({ data: frame });
        while (this.frames.length > AUDIO.stream.maxBufferedFrames) {
            const oldFrame = this.frames.shift()!;
            if (oldFrame.data.buffer.byteLength === AUDIO.encode.frameBufferBytes)
                bufferPool.release(oldFrame.data.buffer);
        }
        this.frameAdded.trigger();
    }

    private async stream(): Promise<void> {
        if (this.streamAfter != null) {
            await this.streamAfter;
            this.streamAfter = undefined;
        }

        if (this.isDisposed)
            return;
        if (this.isCompleted && this.frames.length === 0)
            return;

        // Retry loop for peer-change recovery:
        //
        // PushAudio is configured with RpcRemoteExecutionMode.AwaitForConnection | AllowReconnect,
        // and the RpcStream has allowReconnect=true (default). On a same-peer WS reconnect, Fusion
        // transparently resumes the stream via $sys.Reconnect + $sys.Ack(MustReset=true) — the
        // sender stays alive, no iteration of this loop happens.
        //
        // On peer-change, sharedObjects.disconnectAll() disposes the sender; it calls
        // iterator.return() on the generator, which unwinds try/finally (freeing the in-flight
        // frame buffer) and exits. whenSent resolves. We then loop: create a brand-new PushAudio
        // call (and thus a new server-side chat entry) carrying whatever frames accumulated in
        // `this.frames` during the reconnect, plus any future frames up to completion. This is
        // the "send unsent as a new stream on peer-change" contract.
        //
        // Natural termination (source returned after `isCompleted && frames empty`) also resolves
        // whenSent; the loop guard handles that case.
        const self = this;
        let sentFrameCount = 0;
        try {
            while (!this.isDisposed) {
                if (this.isCompleted && this.frames.length === 0)
                    return;

                await AudioStreamer.ensureConnected();
                if (this.isDisposed)
                    return;

                const liveAudioStreams = streamingApi.liveAudioStreams;
                const peer = Api.peer;

                // frameIndex is per-PushAudio, so it resets; the claimed start doesn't - it
                // advances by what earlier calls sent. Repeating it makes the server register the
                // retry with a BeginsAt equal to the dead call's, and the muxer keeps the dead one.
                // debugOffsetMs is a DebugUI knob to simulate drift; in production it's 0.
                let frameIndex = 0;
                const sourceStartedAtMs = (this.sourceStartedAtMs ?? ServerClock.now())
                    + sentFrameCount * AUDIO.frameDurationMs;
                const sourceStartOffsetSeconds = (sourceStartedAtMs + AudioStreamer.debugOffsetMs) / 1000;
                infoLog?.log(`${this.name}: PushAudio sourceStartOffset=${sourceStartOffsetSeconds.toFixed(3)}s ` +
                    `(sourceStartedAtMs=${sourceStartedAtMs.toFixed(0)}, ` +
                    `debugOffsetMs=${AudioStreamer.debugOffsetMs})`);

                // Recording RPC stream: non-realtime, no compaction, explicit ACK cadence.
                // Termination is driven by iterator.return() from RpcStreamSender.disconnect()
                // (peer-change or final stop); the try/finally below ensures the pooled buffer is
                // returned even if the generator is force-closed during a yield.
                const stream = new RpcStream<AudioFrameDto>(
                    (async function* () {
                        for (;;) {
                            const item = self.frames.shift();
                            const frame = item?.data;
                            if (frame) {
                                try {
                                    yield {
                                        Data: frame,
                                        Offset: toMoment(frameIndex * AUDIO.frameDurationTicks),
                                        Duration: toMoment(AUDIO.frameDurationTicks),
                                        IsKeyFrame: true,
                                    };
                                    frameIndex++;
                                } finally {
                                    // Release pooled buffer even on iterator.return()/throw
                                    if (frame.buffer.byteLength === AUDIO.encode.frameBufferBytes)
                                        bufferPool.release(frame.buffer);
                                }
                            } else if (self.isCompleted && self.frames.length === 0) {
                                infoLog?.log(`${self.name}: stream completed, ${frameIndex} frames sent`);
                                return;
                            } else {
                                await self.frameAdded.whenNext();
                            }
                        }
                    })(),
                    MediaRpcStreamOptions.audioRecording<AudioFrameDto>(),
                );

                void liveAudioStreams
                    .PushStream(RPC_SESSION_DEFAULT, this.chatId, this.repliedChatEntryId ?? null,
                        sourceStartOffsetSeconds, this.preSkip, stream.toRef(peer))
                    .catch((err: unknown) => warnLog?.log(`${this.name}: PushStream rejected:`, err))
                    .finally(() => stream.disconnect());

                // repliedChatEntryId is only meaningful for the very first segment of the
                // recording; subsequent peer-change-induced segments must not re-reply.
                this.repliedChatEntryId = undefined;

                await stream.whenSent;
                sentFrameCount += frameIndex;
                if (this.isDisposed || (this.isCompleted && this.frames.length === 0))
                    return;

                // Only once we know this is a retry: addStream chains each stream on the previous
                // one's whenDisposed, so an unconditional delay pushes back every later utterance.
                await delayAsync(AUDIO.stream.streamErrorRetryDelayMs);
            }
        } catch (error) {
            warnLog?.log(`${this.name}: stream error:`, error);
        } finally {
            this.dispose();
        }
    }
}

export class AudioStreamer {
    public static readonly streams = new Array<AudioStream>();
    public static lastStream: AudioStream | null = null;
    public static connectionStateChangedEvents = new EventHandlerSet<boolean>()
    /** Debug-only: ms added to the recorder's reported source start timestamp
     *  on every new PushStream. Set via DebugUI.setAudioRecorderOffset to
     *  simulate audio drift relative to real time. Default 0. */
    public static debugOffsetMs = 0;

    private static _initialized = false;

    /** Api module that installs a `defaultPeerFactory` wiring connect/disconnect
     *  handlers to `updateConnectionState` before the reconnect loop starts —
     *  otherwise the first connect event can fire before listeners are attached. */
    private static readonly _connectionStateTracker: ApiModule = {
        deps: [streamingApi],
        register(hub) {
            hub.defaultPeerFactory = (h, r) => {
                const peer = Api.configurePeer(new RpcClientPeer(h, r, false));
                peer.connectionStateChanged.add(state => {
                    const isConnected = state === RpcConnectionState.Connected;
                    updateConnectionState(isConnected);
                    // if (isConnected) {
                    //     void coreApi.systemProperties.GetServerApiInfoNC('')
                    //         .then(info => infoLog?.log('post-connect GetServerApiInfoNC:', info))
                    //         .catch(e => warnLog?.log('post-connect GetServerApiInfoNC rejected:', e));
                    // }
                });
                peer.start();
                return peer;
            };
        },
    };

    public static init(apiUrl: string, sessionTokenProvider?: SessionTokenProvider): void {
        if (this._initialized)
            return;

        debugLog?.log(`init`, apiUrl);

        bufferPool = new ObjectPool<ArrayBufferLike>(
            () => new ArrayBuffer(AUDIO.encode.frameBufferBytes)
        ).expandTo(20);

        Api.init('AudioRecorder', {
            url: apiUrl,
            modules: [this._connectionStateTracker],
            connectivityUI: WorkerConnectivityUI,
            sessionTokenProvider,
            requireConnection: true,
        });
        // Audio streamer always wants the peer up — held for the worker's
        // lifetime. The .NET-connected side still gates actual attempts.
        this._initialized = true;
    }

    public static get isInitialized(): boolean {
        return this._initialized;
    }

    public static get isConnected(): boolean {
        return this._initialized && Api.peer.isConnected;
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

    public static addStream(preSkip: number, chatId: string, repliedChatEntryId: string): AudioStream {
        let stream: AudioStream;
        if (this.streams.length < AUDIO.stream.maxStreams) {
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
