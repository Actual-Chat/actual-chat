import { RingBuffer } from "./ring-buffer";
import { VadMessage } from "../audio-vad-message";

const SamplesPerWindow = 512;

export class VadAudioWorkletProcessor extends AudioWorkletProcessor {
    private buffer: RingBuffer;
    private bufferDeque: ArrayBuffer[];

    private workerPort: MessagePort;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.init();
        this.port.onmessage = (ev) => {
            const { topic }: VadMessage = ev.data;

            switch (topic) {
                case 'init-port':
                    this.init();
                    this.workerPort = ev.ports[0];
                    this.workerPort.onmessage = this.onWorkerMessage.bind(this);
                    break;
                default:
                    break;
            }
        };
    }

    private init(): void {
        this.buffer = new RingBuffer(2048, 1);
        this.bufferDeque = [];
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: { [name: string]: Float32Array; }): boolean {

        // if we are disconnected from input/output (node,channel) then we can be closed
        if (inputs == null
            || inputs.length === 0
            || inputs[0].length === 0
            || outputs == null
            || outputs.length === 0
            || outputs[0].length === 0)
            return false;

        const input = inputs[0];
        const output = outputs[0];

        for (let channel = 0; channel < input.length; channel++) {
            const inputChannel = input[channel];
            const outputChannel = output[channel];
            outputChannel.set(inputChannel);
        }

        this.buffer.push(input);
        if (this.buffer.framesAvailable >= SamplesPerWindow) {
            const vadBuffer = [];
            let vadArrayBuffer = this.bufferDeque.shift();
            if (vadArrayBuffer === undefined) {
                vadArrayBuffer = new ArrayBuffer(SamplesPerWindow * 4);
            }

            vadBuffer.push(new Float32Array(vadArrayBuffer, 0, SamplesPerWindow));

            if (this.buffer.pull(vadBuffer)) {
                if (this.workerPort !== undefined) {
                    this.workerPort.postMessage({ topic: 'buffer', buffer: vadArrayBuffer }, [vadArrayBuffer]);
                } else {
                    console.log('worklet port is still undefined');
                }
            } else {
                this.bufferDeque.unshift(vadArrayBuffer);
            }
        }

        return true;
    }

    private onWorkerMessage(ev: MessageEvent<VadMessage>) {
        const { topic, buffer } = ev.data;

        switch (topic) {
            case 'buffer':
                this.bufferDeque.push(buffer);
                break;
            default:
                break;
        }
    }
}

// @ts-ignore
registerProcessor('audio-vad-worklet-processor', VadAudioWorkletProcessor);
