import { RingBuffer } from "./ring-buffer";
import { VadMessage } from "../audio-vad.message";

const SamplesPerWindow = 4000;

export class VadAudioWorkletProcessor extends AudioWorkletProcessor {
    private _buffer: RingBuffer;
    private _bufferDeque: ArrayBuffer[];

    private _workerPort: MessagePort;

    static get parameterDescriptors() {
        return [];
    }

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.init();
        this.port.onmessage = (ev) => {
            const { topic }: VadMessage = ev.data;

            switch (topic) {
                case 'init-port':
                    this.init();
                    this._workerPort = ev.ports[0];
                    this._workerPort.onmessage = this.onWorkerMessage.bind(this);
                    break;
                default:
                    break;
            }
        };
    }

    private init(): void {
        this._buffer = new RingBuffer(8192, 1);
        this._bufferDeque = [];
        this._bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this._bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this._bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this._bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: { [name: string]: Float32Array; }): boolean {
        if (inputs == null
            || inputs.length === 0
            || inputs[0].length === 0
            || outputs == null
            || outputs.length === 0)
            return false;
        const input = inputs[0];
        const output = outputs[0];

        for (let channel = 0; channel < input.length; channel++) {
            const inputChannel = input[channel];
            const outputChannel = output[channel];
            outputChannel.set(inputChannel);
        }

        this._buffer.push(input);
        if (this._buffer.framesAvailable >= SamplesPerWindow) {
            const vadBuffer = [];
            let vadArrayBuffer = this._bufferDeque.shift();
            if (vadArrayBuffer === undefined) {
                vadArrayBuffer = new ArrayBuffer(SamplesPerWindow * 4);
            }

            vadBuffer.push(new Float32Array(vadArrayBuffer, 0, SamplesPerWindow));

            if (this._buffer.pull(vadBuffer)) {
                if (this._workerPort !== undefined) {
                    this._workerPort.postMessage({ topic: 'buffer', buffer: vadArrayBuffer }, [vadArrayBuffer]);
                } else {
                    console.log('worklet port is still undefined');
                }
            } else {
                this._bufferDeque.unshift(vadArrayBuffer);
            }
        }

        return true;
    }

    private onWorkerMessage(ev: MessageEvent<VadMessage>) {
        const { topic, buffer } = ev.data;

        switch (topic) {
            case 'buffer':
                this._bufferDeque.push(buffer);
            default:
                break;
        }
    }
}
