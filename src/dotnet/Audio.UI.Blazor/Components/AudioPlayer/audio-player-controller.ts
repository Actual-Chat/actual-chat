import { FeederAudioWorkletNode, PlaybackState } from './worklets/feeder-audio-worklet-node';
import { CreateDecoderMessage, DataDecoderMessage, DecoderWorkerMessage, EndDecoderMessage, InitDecoderMessage, OperationCompletedDecoderWorkerMessage, StopDecoderMessage } from './workers/opus-decoder-worker-message';
import { Resettable } from 'object-pool';
import { AudioContextPool } from 'audio-context-pool';
import { isAecWorkaroundNeeded, enableChromiumAec } from './chromiumEchoCancellation';

const worker = new Worker('/dist/opusDecoderWorker.js');
let workerLastCallbackId = 0;
const workerCallbacks = new Map<number, () => void>();

worker.onmessage = (ev: MessageEvent<DecoderWorkerMessage>) => {
    const msg = ev.data;
    try {
        switch (msg.type) {
            case 'operationCompleted':
                onOperationCompleted(msg as OperationCompletedDecoderWorkerMessage);
                break;
            default:
                throw new Error(`Unsupported message from the decoder worker. Message type: ${msg.type}`);
        }
    }
    catch (error) {
        console.error(error);
    }
};

function onOperationCompleted(message: OperationCompletedDecoderWorkerMessage) {
    const { callbackId: id } = message;
    const callback = workerCallbacks.get(id);
    if (callback === undefined) {
        throw new Error(`Decoder worker: callback with id '${id}' is not found.`);
    }
    workerCallbacks.delete(id);
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
    private audioElement?: HTMLAudioElement = null;
    private destinationNode?: MediaStreamAudioDestinationNode = null;

    private constructor() {
        this.id = lastControllerId++;
        console.warn(`created controllerId:${this.id}`);
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

    public async init(header: Uint8Array, callbacks: {
        onBufferTooMuch?: () => void,
        onBufferLow?: () => void,
        onStartPlaying?: () => void,
        onStarving?: () => void,
        /** If playing was started and now it's stopped */
        onStopped?: () => void,
        /** Called at the end of the queue, even if the playing wasn't started */
        onEnded?: () => void,
    }): Promise<void> {
        if (this.feederNode === null) {
            await this.createNodes();
        }
        const { feederNode, audioContext, destinationNode, audioElement } = this;
        feederNode.onBufferLow = callbacks.onBufferLow;
        feederNode.onStartPlaying = callbacks.onStartPlaying;
        feederNode.onBufferTooMuch = callbacks.onBufferTooMuch;
        feederNode.onStarving = callbacks.onStarving;
        feederNode.onStopped = callbacks.onStopped;
        feederNode.onEnded = callbacks.onEnded;

        // we should use isAecWorkaroundNeeded() rather this.audioElement !== null
        // because we should take an option to disable it
        if (isAecWorkaroundNeeded()) {
            feederNode.connect(destinationNode);
            const stream = await enableChromiumAec(destinationNode.stream);
            audioElement.srcObject = stream;
            audioElement.muted = false;
            const _ = audioElement.play();
        }
        else {
            feederNode.connect(audioContext.destination);
        }
        await this.initWorker(header);
    }

    /** The second phase of initialization, after a user gesture we can create an audio context and worklet objects */
    private async createNodes(): Promise<void> {
        this.audioContext = await AudioContextPool.get('main') as AudioContext;
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

        if (isAecWorkaroundNeeded()) {
            this.destinationNode = this.audioContext.createMediaStreamDestination();
            this.audioElement = new Audio();
            this.audioElement.autoplay = false;
            this.audioElement.muted = true;
            this.audioElement.pause();
        }
    }

    private initWorker(header: Uint8Array): Promise<void> {
        const callbackId = workerLastCallbackId++;
        return new Promise<void>(resolve => {
            workerCallbacks.set(callbackId, resolve);
            const msg: InitDecoderMessage = {
                type: 'init',
                controllerId: this.id,
                callbackId: callbackId,
                buffer: header.buffer,
                length: header.byteLength,
                offset: header.byteOffset,
            };
            worker.postMessage(msg, [msg.buffer]);
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
        console.assert(this.feederNode !== null, 'Feeder node should be created. Lifetime error.');
        return this.feederNode.getState();
    }

    public stop(): void {
        console.assert(this.feederNode !== null, 'Feeder node should be created. Lifetime error.');
        const workerMsg: StopDecoderMessage = {
            type: 'stop',
            controllerId: this.id,
        };
        worker.postMessage(workerMsg);
        this.feederNode.stop();
        if (this.audioElement !== null) {
            this.audioElement.muted = true;
            this.audioElement.pause();
        }
        // we sent the stop to worker and worklet (node->processor->onStopped->release to the pool->reset)
    }

    public reset(): void | PromiseLike<void> {
        const { feederNode, destinationNode, audioElement } = this;
        if (feederNode !== null) {
            feederNode.disconnect();
            feederNode.onBufferLow = null;
            feederNode.onStartPlaying = null;
            feederNode.onBufferTooMuch = null;
            feederNode.onStarving = null;
            feederNode.onStopped = null;
            feederNode.onEnded = null;
        }
        if (destinationNode !== null) {
            destinationNode.disconnect();
        }
        if (audioElement !== null) {
            audioElement.muted = true;
            audioElement.pause();
        }
    }
}
