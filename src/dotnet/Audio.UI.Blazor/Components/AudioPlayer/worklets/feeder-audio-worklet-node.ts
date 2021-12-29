import { ChangeStateNodeMessage, DataNodeMessage, GetStateNodeMessage, NodeMessage, ProcessorMessage, StateChangedProcessorMessage, StateProcessorMessage } from "./feeder-audio-worklet-message";

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode {
    public onBufferLow?: () => void = null;
    public onStarving?: () => void = null;

    private lastCallbackId: number = 0;
    private callbacks = new Map<number, Function>();

    constructor(context: BaseAudioContext, name: string, options?: AudioWorkletNodeOptions) {
        super(context, name, options);
        this.onprocessorerror = this.onProcessorError;
        this.port.onmessage = this.onProcessorMessage;
    }

    public play(): void {
        const msg: ChangeStateNodeMessage = {
            type: "changeState",
            state: "play",
        };
        this.port.postMessage(msg);
    }

    public clear(): void {
        const msg: NodeMessage = {
            type: "clear",
        };
        this.port.postMessage(msg);
    }

    public stop(): void {
        const msg: ChangeStateNodeMessage = {
            type: "changeState",
            state: "stop",
        };
        this.port.postMessage(msg);


    }

    public feed(samples: Float32Array) {
        const msg: DataNodeMessage = {
            type: "data",
            buffer: samples.buffer
        };
        this.port.postMessage(msg, [msg.buffer]);
    }

    public getState(): Promise<PlaybackState> {
        const callbackId = this.lastCallbackId++;
        return new Promise<PlaybackState>((resolve, reject) => {
            this.callbacks.set(callbackId, resolve);
            const msg: GetStateNodeMessage = {
                type: "getState",
                id: callbackId,
            };
            this.port.postMessage(msg);

        });
    }

    private onProcessorMessage = (ev: MessageEvent<ProcessorMessage>): void => {
        const msg = ev.data;
        switch (msg.type) {
            case 'stateChanged': {
                this.onStateChanged(msg as StateChangedProcessorMessage);
                break;
            }
            case 'state': {
                this.onState(msg as StateProcessorMessage);
                break;
            }
            default:
                throw new Error(`Feeder node: Unsupported message type: ${msg.type}`);
        }
    };

    private onState(message: StateProcessorMessage): void {
        const resolve = this.callbacks.get(message.id);
        if (resolve === undefined) {
            console.error(`Feeder node: callback with id '${message.id}' is not found.`);
            return;
        }
        const result: PlaybackState = {
            playbackTime: message.playbackTime / 1000.0,
            bufferedDuration: message.sampleCount / 48000.0
        };
        resolve(result);
    }


    private onStateChanged(message: StateChangedProcessorMessage): void {
        if (message.state === 'playingWithLowBuffer' && this.onBufferLow !== null) {
            this.onBufferLow();
        }
        else if (message.state === 'starving' && this.onStarving !== null) {
            this.onStarving();
        }
    }

    private onProcessorError = (ev: Event) => {
        console.error("Feeder node: Unhandled feeder processor error: ", ev);
    };
}

export interface PlaybackState {
    /** playback time in seconds */
    playbackTime: number,
    /** how much seconds do we have in the buffer to play */
    bufferedDuration: number,
}

