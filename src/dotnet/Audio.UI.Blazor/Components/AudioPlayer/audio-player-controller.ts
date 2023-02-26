import { AudioContextRef } from 'audio-context-ref';
import { audioContextSource } from 'audio-context-source';
import { Resettable } from 'resettable';
import { Versioning } from 'versioning';
import { Log, LogLevel, LogScope } from 'logging';
import { AsyncDisposable, Disposable } from 'disposable';
import { rpcClient, rpcNoWait } from 'rpc';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { enableChromiumAec, isAecWorkaroundNeeded } from './chromium-echo-cancellation';
import { PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';

const LogScope: LogScope = 'AudioPlayerController';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

let lastControllerId = 0;

/** The main class of audio player, that controls all parts of the playback */
export class AudioPlayerController implements Resettable, AsyncDisposable {
    private static decoderWorkerInstance: Worker = null;
    private static decoderWorker: OpusDecoderWorker & Disposable = null;

    private decoderChannel: MessageChannel = null;
    private contextRef: AudioContextRef = null;
    private feederNode?: FeederAudioWorkletNode = null;
    private destinationNode?: MediaStreamAudioDestinationNode = null;
    private isAecWorkaroundUsed = isAecWorkaroundNeeded();

    /** The id is used to store related objects on the web worker side */
    public readonly id: number;

    private constructor() {
        this.id = lastControllerId++;
        debugLog?.log(`constructor: #${this.id}`);
    }

    public static async init(): Promise<void> {
        warnLog?.assert(
            this.decoderWorkerInstance === null,
            `init: decoderWorkerInstance has already been created. Lifetime error.`);
        const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
        this.decoderWorkerInstance = new Worker(decoderWorkerPath);
        this.decoderWorker = rpcClient<OpusDecoderWorker>(`${LogScope}.vadWorker`, this.decoderWorkerInstance);
        await this.decoderWorker.create(Versioning.artifactVersions);
    }

    /**
     * Create uninitialized object and registers it on the web worker side.
     */
    public static async create(): Promise<AudioPlayerController> {
        return new AudioPlayerController();
    }

    public async use(callbacks: {
        onBufferTooMuch?: () => void,
        onBufferLow?: () => void,
        onStartPlaying?: () => void,
        onStarving?: () => void,
        onPaused?: () => void;
        onResumed?: () => void;
        onStopped?: () => void,
        onEnded?: () => void,
    }): Promise<void> {
        const onReady = async (context: AudioContext) => {
            // context has been replaced, recreate Node and MessageChannel
            // you can transfer MessagePort only once
            if (this.feederNode && this.feederNode.context !== context) {
                await this.feederNode.stop();
                await AudioPlayerController.decoderWorker.disposeDecoder(this.id);
                this.decoderChannel?.port1.close();
                this.decoderChannel?.port2.close();
                this.feederNode.disconnect();
                this.feederNode.onBufferLow = null;
                this.feederNode.onStartPlaying = null;
                this.feederNode.onBufferTooMuch = null;
                this.feederNode.onStarving = null;
                this.feederNode.onPaused = null;
                this.feederNode.onResumed = null;
                this.feederNode.onStopped = null;
                this.feederNode.onEnded = null;
                this.feederNode = null;
                this.decoderChannel = null;
            }
            // we can reuse exising node and
            if (this.feederNode === null) {
                this.decoderChannel = new MessageChannel();
                const feederNodeOptions: AudioWorkletNodeOptions = {
                    channelCount: 1,
                    channelCountMode: 'explicit',
                    numberOfInputs: 0,
                    numberOfOutputs: 1,
                    outputChannelCount: [1],
                };
                this.feederNode = await FeederAudioWorkletNode.create(
                    this.decoderChannel.port2,
                    context,
                    'feederWorklet',
                    feederNodeOptions,
                );
                // Initialize worker
                await AudioPlayerController.decoderWorker.start(this.id, this.decoderChannel.port1);
            }

            const feederNode = this.feederNode;
            feederNode.onBufferLow = callbacks.onBufferLow;
            feederNode.onStartPlaying = callbacks.onStartPlaying;
            feederNode.onBufferTooMuch = callbacks.onBufferTooMuch;
            feederNode.onStarving = callbacks.onStarving;
            feederNode.onPaused = callbacks.onPaused;
            feederNode.onResumed = callbacks.onResumed;
            feederNode.onStopped = callbacks.onStopped;
            feederNode.onEnded = callbacks.onEnded;

            debugLog?.log(`init: isAecWorkaroundUsed:`, this.isAecWorkaroundUsed);
            if (this.isAecWorkaroundUsed) {
                this.destinationNode = context.createMediaStreamDestination();
                feederNode.connect(this.destinationNode);
                await enableChromiumAec(this.destinationNode.stream);
            } else {
                feederNode.connect(context.destination);
            }
        };
        const onNotReady = async (_context: AudioContext) => {
            if (this.feederNode != null) {
                await this.feederNode.stop();
                this.feederNode.disconnect();
                this.feederNode.onBufferLow = null;
                this.feederNode.onStartPlaying = null;
                this.feederNode.onBufferTooMuch = null;
                this.feederNode.onStarving = null;
                this.feederNode.onPaused = null;
                this.feederNode.onResumed = null;
                this.feederNode.onStopped = null;
                this.feederNode.onEnded = null;
            }
            // TODO: do not recreate destinationNode?
            if (this.destinationNode != null) {
                const tracks = this.destinationNode.stream.getTracks();
                for (let i = 0; i < tracks.length; ++i) {
                    this.destinationNode.stream.removeTrack(tracks[i]);
                }
                this.destinationNode.disconnect();
                this.destinationNode = null;
            }
        };

        if (this.contextRef == null)
            this.contextRef = audioContextSource.getRef(onReady, onNotReady);
        await this.contextRef.whenReady();
    }

    public async reset(): Promise<void> {
        const contextRef = this.contextRef;
        if (contextRef == null)
            return;

        this.contextRef = null;
        await contextRef.disposeAsync();
    }

    public async getState(): Promise<PlaybackState> {
        return this.feederNode.getState();
    }

    public async stop(): Promise<void> {
        await AudioPlayerController.decoderWorker.stop(this.id);
        await this.feederNode.stop();
    }

    public end(): Promise<void> {
        return AudioPlayerController.decoderWorker.end(this.id);
    }

    public pause(): Promise<void> {
        return this.feederNode.pause();
    }

    public resume(): Promise<void> {
        return this.feederNode.resume();
    }

    async disposeAsync(): Promise<void> {
        return AudioPlayerController.decoderWorker.disposeDecoder(this.id);
    }

    public decode(bytes: Uint8Array): void {
        void AudioPlayerController.decoderWorker.onEncodedChunk(
            this.id,
            bytes.buffer,
            bytes.byteOffset,
            bytes.length,
            rpcNoWait);
    }
}
