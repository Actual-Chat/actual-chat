/* eslint-disable @typescript-eslint/ban-types */
import {
    GetStateNodeMessage,
    InitNodeMessage,
    OperationCompletedProcessorMessage,
    ProcessorMessage,
    StateChangedProcessorMessage,
    StateProcessorMessage,
    PauseNodeMessage,
    ResumeNodeMessage,
    StopNodeMessage,
} from './feeder-audio-worklet-message';

const LogScope: string = 'FeederNode';

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode {

    public onStartPlaying?: () => void = null;
    public onBufferLow?: () => void = null;
    public onBufferTooMuch?: () => void = null;
    public onStarving?: () => void = null;
    public onPaused?: () => void = null;
    public onResumed?: () => void = null;
    /** If playing was started and now it's stopped */
    public onStopped?: () => void = null;
    /** Called at the end of the queue, even if the playing wasn't started */
    public onEnded?: () => void = null;

    private lastCallbackId = 0;
    private callbacks = new Map<number, Function>();

    private constructor(context: BaseAudioContext, name: string, options?: AudioWorkletNodeOptions) {
        super(context, name, options);
        this.onprocessorerror = this.onProcessorError;
        this.port.onmessage = this.onProcessorMessage;
    }

    public static async create(
        decoderWorkerPort: MessagePort,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ): Promise<FeederAudioWorkletNode> {

        const node = new FeederAudioWorkletNode(context, name, options);
        const callbackId = node.lastCallbackId++;
        return new Promise<FeederAudioWorkletNode>(resolve => {
            node.callbacks.set(callbackId, () => resolve(node));
            const msg: InitNodeMessage = {
                type: 'init',
                callbackId: callbackId,
                decoderWorkerPort: decoderWorkerPort,
            };
            node.port.postMessage(msg, [decoderWorkerPort]);
        });
    }

    public stop(): void {
        const msg: StopNodeMessage = { type: 'stop' };
        this.port.postMessage(msg);
    }

    public pause(): void {
        const msg: PauseNodeMessage = { type: 'pause' };
        this.port.postMessage(msg);
    }

    public resume(): void {
        const msg: ResumeNodeMessage = { type: 'resume' };
        this.port.postMessage(msg);
    }

    public getState(): Promise<PlaybackState> {
        const callbackId = this.lastCallbackId++;
        return new Promise<PlaybackState>(resolve => {
            this.callbacks.set(callbackId, resolve);
            const msg: GetStateNodeMessage = {
                type: 'getState',
                callbackId: callbackId,
            };
            this.port.postMessage(msg);

        });
    }

    private onProcessorMessage = (ev: MessageEvent<ProcessorMessage>): void => {
        const msg = ev.data;
        try {
            switch (msg.type) {
            case 'stateChanged':
                this.onStateChanged(msg as StateChangedProcessorMessage);
                break;
            case 'state':
                this.onState(msg as StateProcessorMessage);
                break;
            case 'operationCompleted':
                this.onOperationCompleted(msg as OperationCompletedProcessorMessage);
                break;
            default:
                throw new Error(`Unsupported message type: ${msg.type}`);
            }
        }
        catch (error) {
            console.error(error);
        }
    };

    private popCallback(callbackId: number): Function {
        const callback = this.callbacks.get(callbackId);
        if (callback === undefined) {
            throw new Error(`Callback #${callbackId} is not found.`);
        }
        this.callbacks.delete(callbackId);
        return callback;
    }

    private onOperationCompleted(message: OperationCompletedProcessorMessage): void {
        const callback = this.popCallback(message.callbackId);
        callback();
    }

    private onState(message: StateProcessorMessage): void {
        const callback = this.popCallback(message.callbackId);
        const result: PlaybackState = {
            playbackTime: message.playbackTime,
            bufferedTime: message.bufferedTime,
        };
        callback(result);
    }

    private onStateChanged(message: StateChangedProcessorMessage): void {
        if (message.state === 'playingWithLowBuffer' && this.onBufferLow !== null) {
            this.onBufferLow();
        }
        else if (message.state === 'starving' && this.onStarving !== null) {
            this.onStarving();
        }
        else if (message.state === 'playingWithTooMuchBuffer' && this.onBufferTooMuch !== null) {
            this.onBufferTooMuch();
        }
        else if (message.state === 'playing' && this.onStartPlaying !== null) {
            this.onStartPlaying();
        }
        else if (message.state === 'stopped' && this.onStopped !== null) {
            this.onStopped();
        }
        else if (message.state === 'ended' && this.onEnded !== null) {
            this.onEnded();
        }
        else if (message.state === 'paused' && this.onPaused !== null) {
            this.onPaused();
        }
        else if (message.state === 'resumed' && this.onResumed !== null) {
            this.onResumed();
        }
    }

    private onProcessorError = (ev: Event) => {
        console.error(`${LogScope}: Unhandled feeder processor error:`, ev);
    };
}

export interface PlaybackState {
    /** In seconds from the start of playing, excluding starving time and processing time */
    playbackTime: number,
    /** how much seconds do we have in the buffer to play. */
    bufferedTime: number,
}

