import Denque from 'denque';
import { AudioRingBuffer } from './audio-ring-buffer';
import { BufferVadWorkletMessage, VadWorkletMessage } from './audio-vad-worklet-message';
import { Log, LogLevel } from 'logging';

const LogScope = 'VadAudioWorkletProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SAMPLES_PER_WINDOW = 768;

export class VadAudioWorkletProcessor extends AudioWorkletProcessor {
    private buffer: AudioRingBuffer;
    private bufferDeque: Denque<ArrayBuffer>;

    private workerPort: MessagePort;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.init();
        this.port.onmessage = this.onRecorderMessage;
    }

    private init(): void {
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferDeque = new Denque<ArrayBuffer>();
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        if (inputs == null
            || inputs.length === 0
            || inputs[0].length === 0
            || outputs == null
            || outputs.length === 0
            || outputs[0].length === 0)
            return true;

        const input = inputs[0];
        const output = outputs[0];

        for (let channel = 0; channel < input.length; channel++) {
            const inputChannel = input[channel];
            const outputChannel = output[channel];
            outputChannel.set(inputChannel);
        }

        this.buffer.push(input);
        if (this.buffer.framesAvailable >= SAMPLES_PER_WINDOW) {
            const vadBuffer = new Array<Float32Array>();
            let vadArrayBuffer = this.bufferDeque.shift();
            if (vadArrayBuffer === undefined) {
                vadArrayBuffer = new ArrayBuffer(SAMPLES_PER_WINDOW * 4);
            }

            vadBuffer.push(new Float32Array(vadArrayBuffer, 0, SAMPLES_PER_WINDOW));

            if (this.buffer.pull(vadBuffer)) {
                if (this.workerPort !== undefined) {
                    const bufferMessage: BufferVadWorkletMessage = {
                        type: 'buffer',
                        buffer: vadArrayBuffer,
                    };
                    this.workerPort.postMessage(bufferMessage, [vadArrayBuffer]);
                } else {
                    warnLog?.log('process: worklet port is still undefined!');
                }
            } else {
                this.bufferDeque.unshift(vadArrayBuffer);
            }
        }

        return true;
    }

    private onWorkerMessage = (ev: MessageEvent<BufferVadWorkletMessage>) => {
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
    };

    private onRecorderMessage = (ev: MessageEvent<VadWorkletMessage>) => {
        try {
            const { type } = ev.data;

            switch (type) {
            case 'init':
                this.init();
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

// @ts-expect-error  - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('audio-vad-worklet-processor', VadAudioWorkletProcessor);
