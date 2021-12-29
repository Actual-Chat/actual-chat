import Denque from 'denque';
import { RingBuffer } from "./ring-buffer";

const SamplesPerWindow = 1920;

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor {
    private readonly timeslice: number;

    private buffer: RingBuffer;
    private bufferDeque: Denque;

    private workerPort: MessagePort;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        const { timeslice } = options.processorOptions;
        this.timeslice = timeslice;

        this.init();
        this.port.onmessage = (ev) => {
            const { topic } = ev.data;

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
        this.buffer = new RingBuffer(8192, 1);
        this.bufferDeque = new Denque<ArrayBuffer>();
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(SamplesPerWindow * 4));
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: { [name: string]: Float32Array; }): boolean {
        try {
            if (inputs == null
                || inputs.length === 0
                || inputs[0].length === 0
                || outputs == null
                || outputs.length === 0)
                return true;
            const input = inputs[0];
            const output = outputs[0];

            for (let channel = 0; channel < input.length; channel++) {
                const inputChannel = input[channel];
                const outputChannel = output[channel];
                outputChannel.set(inputChannel);
            }

            this.buffer.push(input);
            if (this.buffer.framesAvailable >= SamplesPerWindow) {
                const audioBuffer = [];
                let audioArrayBuffer = this.bufferDeque.shift();
                if (audioArrayBuffer === undefined) {
                    audioArrayBuffer = new ArrayBuffer(SamplesPerWindow * 4);
                }

                audioBuffer.push(new Float32Array(audioArrayBuffer, 0, SamplesPerWindow));

                if (this.buffer.pull(audioBuffer)) {
                    if (this.workerPort !== undefined) {
                        this.workerPort.postMessage({ topic: 'buffer', buffer: audioArrayBuffer }, [audioArrayBuffer]);
                    } else {
                        console.log('worklet port is still undefined');
                    }
                } else {
                    this.bufferDeque.unshift(audioArrayBuffer);
                }
            }
        }
        catch(error) {
            console.log(error);
        }

        return true;
    }

    private onWorkerMessage(ev: MessageEvent) {
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
registerProcessor('opus-encoder-worklet-processor', OpusEncoderWorkletProcessor);
