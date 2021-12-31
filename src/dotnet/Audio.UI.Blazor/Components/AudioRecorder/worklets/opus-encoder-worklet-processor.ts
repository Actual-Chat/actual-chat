import Denque from 'denque';
import {AudioRingBuffer} from "./audio-ring-buffer";

const SamplesPerMs = 48;

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor {
    private readonly samplesPerWindow: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferDeque: Denque;

    private workerPort: MessagePort;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        const { timeslice } = options.processorOptions;
        if (timeslice != 20 && timeslice != 40 && timeslice != 60 && timeslice != 80) {
            throw new Error('OpusEncoderWorkletProcessor supports only 20, 40, 60, 80 timeslice argument');
        }

        this.samplesPerWindow = timeslice * SamplesPerMs;
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferDeque = new Denque<ArrayBuffer>();
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.port.onmessage = (ev) => {
            const { topic } = ev.data;

            switch (topic) {
                case 'init-port':
                    this.workerPort = ev.ports[0];
                    this.workerPort.onmessage = this.onWorkerMessage.bind(this);
                    break;
                default:
                    break;
            }
        };
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
            if (this.buffer.framesAvailable >= this.samplesPerWindow) {
                const audioBuffer = [];
                let audioArrayBuffer = this.bufferDeque.shift();
                if (audioArrayBuffer === undefined) {
                    audioArrayBuffer = new ArrayBuffer(this.samplesPerWindow * 4);
                }

                audioBuffer.push(new Float32Array(audioArrayBuffer, 0, this.samplesPerWindow));

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
