import Denque from 'denque';
import { AudioRingBuffer } from './audio-ring-buffer';
import { BufferEncoderWorkletMessage, EncoderWorkletMessage } from './opus-encoder-worklet-message';
import { Log, LogLevel } from 'logging';

const LogScope = 'OpusEncoderWorkletProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SAMPLES_PER_MS = 48;

export interface ProcessorOptions {
    timeSlice: number;
}

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor {
    private static allowedTimeSlice = [20, 40, 60, 80];
    private readonly samplesPerWindow: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferDeque: Denque<ArrayBuffer>;

    private workerPort: MessagePort;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        const { timeSlice } = options.processorOptions as ProcessorOptions;

        if (!OpusEncoderWorkletProcessor.allowedTimeSlice.some(val => val === timeSlice)) {
            const allowedTimeSliceJson = JSON.stringify(OpusEncoderWorkletProcessor.allowedTimeSlice);
            throw new Error(`OpusEncoderWorkletProcessor supports only ${ allowedTimeSliceJson } options as timeSlice argument.`);
        }

        this.samplesPerWindow = timeSlice * SAMPLES_PER_MS;
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferDeque = new Denque<ArrayBuffer>();
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.bufferDeque.push(new ArrayBuffer(this.samplesPerWindow * 4));
        this.port.onmessage = this.onRecorderMessage;
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
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
                const audioBuffer = new Array<Float32Array>();
                let audioArrayBuffer = this.bufferDeque.shift();
                if (audioArrayBuffer === undefined) {
                    audioArrayBuffer = new ArrayBuffer(this.samplesPerWindow * 4);
                }

                audioBuffer.push(new Float32Array(audioArrayBuffer, 0, this.samplesPerWindow));

                if (this.buffer.pull(audioBuffer)) {
                    if (this.workerPort !== undefined) {
                        const bufferMessage: BufferEncoderWorkletMessage = {
                            type: 'buffer',
                            buffer: audioArrayBuffer,
                        };
                        this.workerPort.postMessage(bufferMessage, [audioArrayBuffer]);
                    } else {
                        warnLog?.log('process: worklet port is still undefined!');
                    }
                } else {
                    this.bufferDeque.unshift(audioArrayBuffer);
                }
            }
        }
        catch (error) {
            errorLog?.log(`process: unhandled error:`, error);
        }

        return true;
    }

    private onWorkerMessage = (ev: MessageEvent<BufferEncoderWorkletMessage>) => {
        try {
            const { type, buffer } = ev.data;

            switch (type) {
            case 'buffer':
                this.bufferDeque.push(buffer);
                break;
            default:
                break;
            }
        }
        catch (error) {
            errorLog?.log(`onWorkerMessage: unhandled error:`, error);
        }
    }

    private onRecorderMessage = (ev: MessageEvent<EncoderWorkletMessage>) => {
        try {
            const { type } = ev.data;

            switch (type) {
            case 'init':
                this.workerPort = ev.ports[0];
                this.workerPort.onmessage = this.onWorkerMessage;
                break;
            default:
                break;
            }
        }
        catch (error) {
            errorLog?.log(`onRecorderMessage: unhandled error:`, error);
        }
    }
}

// @ts-expect-error - register  is defined
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('opus-encoder-worklet-processor', OpusEncoderWorkletProcessor);
