import { FeederAudioWorkletNode, PlaybackState } from './worklets/feeder-audio-worklet-node';
import { CreateDecoderMessage, DataDecoderMessage, DecoderWorkerMessage, EndDecoderMessage, InitDecoderMessage, OperationCompletedDecoderWorkerMessage, StopDecoderMessage } from './workers/opus-decoder-worker-message';
import { Resettable } from 'object-pool';
import { audioContextLazy } from 'audio-context-lazy';
import { isAecWorkaroundNeeded, enableChromiumAec } from './chromium-echo-cancellation';
import { Log, LogLevel } from 'logging';

const LogScope = 'AudioPlayerController';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const worker = new Worker('/dist/opusDecoderWorker.js');
const workerCallbacks = new Map<number, () => void>();
let workerLastCallbackId = 0;

worker.onmessage = (ev: MessageEvent<DecoderWorkerMessage>) => {
    const msg = ev.data;
    try {
        switch (msg.type) {
        case 'operationCompleted':
            onOperationCompleted(msg as OperationCompletedDecoderWorkerMessage);
            break;
        default:
            // noinspection ExceptionCaughtLocallyJS
            throw new Error(`Unsupported message type: ${msg.type}`);
        }
    }
    catch (error) {
        errorLog?.log(`worker.onmessage: unhandled error:`, error);
    }
};

function onOperationCompleted(message: OperationCompletedDecoderWorkerMessage) {
    const { callbackId: callbackId } = message;
    const callback = workerCallbacks.get(callbackId);
    if (callback === undefined)
        throw new Error(`Callback #${callbackId} is not found.`);

    workerCallbacks.delete(callbackId);
    callback();
}

let lastControllerId = 0;

/** The main class of audio player, that controls all parts of the playback */
export class AudioPlayerController implements Resettable {
    /** The id is used to store related objects on the web worker side */
    public readonly id: number;
    private audioContext?: AudioContext = null;
    private decoderChannel = new MessageChannel();
    private feederNode?: FeederAudioWorkletNode = null;
    private destinationNode?: MediaStreamAudioDestinationNode = null;
    private cleanup?: () => void = null;
    private isReset = false;

    private constructor() {
        this.id = lastControllerId++;
        debugLog?.log(`.ctor: #${this.id}`);
    }

    /**
     * Create uninitialized object and registers it on the web worker side.
     * You should call initialize (to create audioContext, etc) later (after an user gesture action).
     */
    public static create(): Promise<AudioPlayerController> {
        const callbackId = workerLastCallbackId++;
        return new Promise<AudioPlayerController>(resolve => {
            const controller = new AudioPlayerController();
            workerCallbacks.set(callbackId, () => resolve(controller));
            const msg: CreateDecoderMessage = {
                type: 'create',
                controllerId: controller.id,
                callbackId: callbackId,
                workletPort: controller.decoderChannel.port1
            };
            // let's create a decoder object on the web worker side
            worker.postMessage(msg, [controller.decoderChannel.port1]);
        });
    }
    /**
     * Setups create audio worklet nodes if needed and setups callbacks.
     * You should initialize @type { AudioPlayerController } close to playing, after an user gesture
     * because we might use the WebRTC workaround of echo cancellation bugs in chrome and so on.
     */
    public async init(callbacks: {
        onBufferTooMuch?: () => void,
        onBufferLow?: () => void,
        onStartPlaying?: () => void,
        onStarving?: () => void,
        onPaused?: () => void;
        onResumed?: () => void;
        /** If playing was started and now it's stopped */
        onStopped?: () => void,
        /** Called at the end of the queue, even if the playing wasn't started */
        onEnded?: () => void,
    }): Promise<void> {
        this.isReset = false;
        /** The second phase of initialization, after a user gesture we can create an audio context and worklet objects */
        this.audioContext = await audioContextLazy.get();
        if (this.feederNode === null) {
            const feederNodeOptions: AudioWorkletNodeOptions = {
                channelCount: 1,
                channelCountMode: 'explicit',
                numberOfInputs: 0,
                numberOfOutputs: 1,
                outputChannelCount: [1],
            };
            this.feederNode = await FeederAudioWorkletNode.create(
                this.decoderChannel.port2,
                this.audioContext,
                'feederWorklet',
                feederNodeOptions
            );
        }
        const { feederNode, audioContext } = this;
        feederNode.onBufferLow = callbacks.onBufferLow;
        feederNode.onStartPlaying = callbacks.onStartPlaying;
        feederNode.onBufferTooMuch = callbacks.onBufferTooMuch;
        feederNode.onStarving = callbacks.onStarving;
        feederNode.onPaused = callbacks.onPaused;
        feederNode.onResumed = callbacks.onResumed;
        feederNode.onStopped = callbacks.onStopped;
        feederNode.onEnded = callbacks.onEnded;

        // recreating nodes due to memory leaks in not disconnected nodes
        if (isAecWorkaroundNeeded()) {
            debugLog?.log(`init: isAecWorkaroundNeeded() == true`);
            this.destinationNode = this.audioContext.createMediaStreamDestination();
            feederNode.connect(this.destinationNode);
            this.cleanup = await enableChromiumAec(this.destinationNode.stream);
        }
        else {
            debugLog?.log(`init(): isAecWorkaroundNeeded == false`);
            feederNode.connect(audioContext.destination);
        }
        await this.initWorker();
    }

    private initWorker(): Promise<void> {
        const callbackId = workerLastCallbackId++;
        return new Promise<void>(resolve => {
            workerCallbacks.set(callbackId, resolve);
            const msg: InitDecoderMessage = {
                type: 'init',
                controllerId: this.id,
                callbackId: callbackId,
            };
            worker.postMessage(msg);
        });
    }

    public enqueueEnd(): void {
        const msg: EndDecoderMessage = {
            type: 'end',
            controllerId: this.id,
        };
        worker.postMessage(msg);
    }

    public enqueue(bytes: Uint8Array): void {
        const msg: DataDecoderMessage = {
            type: 'data',
            controllerId: this.id,
            buffer: bytes.buffer,
            length: bytes.byteLength,
            offset: bytes.byteOffset,
        };
        worker.postMessage(msg, [bytes.buffer]);
    }

    public async getState(): Promise<PlaybackState> {
        warnLog?.assert(this.feederNode !== null, `getState: feederNode isn't created yet. Lifetime error.`);
        return this.feederNode.getState();
    }

    public stop(): void {
        warnLog?.assert(this.feederNode !== null, `stop: feederNode isn't created yet. Lifetime error.`);
        const workerMsg: StopDecoderMessage = {
            type: 'stop',
            controllerId: this.id,
        };
        worker.postMessage(workerMsg);
        // we sent the stop to worker and worklet (node->processor->onStopped->release to the pool->reset)
        this.feederNode.stop();
    }

    public pause(): void {
        warnLog?.assert(this.feederNode !== null, `pause: feederNode isn't created yet. Lifetime error.`);
        this.feederNode.pause();
    }

    public resume(): void {
        warnLog?.assert(this.feederNode !== null, `resume: feederNode isn't created yet. Lifetime error.`);
        this.feederNode.resume();
    }

    public reset(): void | PromiseLike<void> {
        this.isReset = true;
        if (this.feederNode != null) {
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
        // TODO: do not recreate destinationNode ?
        if (this.destinationNode != null) {
            const tracks = this.destinationNode.stream.getTracks();
            for (let i = 0; i < tracks.length; ++i) {
                this.destinationNode.stream.removeTrack(tracks[i]);
            }
            this.destinationNode.disconnect();
            this.destinationNode = null;
        }
        if (this.cleanup != null) {
            this.cleanup();
            this.cleanup = null;
        }
        if (this.audioContext != null) {
            this.audioContext = null;
        }
    }
}
