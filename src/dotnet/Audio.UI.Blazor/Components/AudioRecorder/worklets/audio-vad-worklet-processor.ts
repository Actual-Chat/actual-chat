import Denque from 'denque';
import { AudioRingBuffer } from './audio-ring-buffer';
import { AudioVadWorker } from '../workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './audio-vad-worklet-contract';
import { Disposable } from 'disposable';
import { rpcClientServer, rpcServer } from 'rpc';
import { Log, LogLevel, LogScope } from 'logging';
import { timerQueue } from 'timerQueue';

const LogScope: LogScope = 'VadAudioWorkletProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SAMPLES_PER_WINDOW = 768;

export class AudioVadWorkletProcessor extends AudioWorkletProcessor implements AudioVadWorklet {
    private buffer: AudioRingBuffer;
    private bufferDeque: Denque<ArrayBuffer>;
    private server: Disposable;
    private worker: AudioVadWorker & Disposable;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        warnLog?.log('ctor');
        this.server = rpcServer(`${LogScope}.server`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<AudioVadWorker>(`${LogScope}.worker`, workerPort, this);
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferDeque = new Denque<ArrayBuffer>();
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
        this.bufferDeque.push(new ArrayBuffer(SAMPLES_PER_WINDOW * 4));
    }

    public async append(buffer: ArrayBuffer): Promise<void> {
        this.bufferDeque.push(buffer);
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        timerQueue?.triggerExpired();
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
                if (this.worker)
                    void this.worker.append(vadArrayBuffer);
                else
                    warnLog?.log('process: worklet port is still undefined!');
            } else {
                this.bufferDeque.unshift(vadArrayBuffer);
            }
        }

        return true;
    }
}

// @ts-expect-error - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('audio-vad-worklet-processor', AudioVadWorkletProcessor);
