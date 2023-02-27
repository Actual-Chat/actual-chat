import Denque from 'denque';
import { AudioRingBuffer } from './audio-ring-buffer';
import { Disposable } from 'disposable';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait, rpcServer } from 'rpc';
import { OpusEncoderWorklet } from './opus-encoder-worklet-contract';
import { OpusEncoderWorker } from '../workers/opus-encoder-worker-contract';
import { Log, LogLevel, LogScope } from 'logging';
import { timerQueue } from 'timerQueue';

const LogScope: LogScope = 'OpusEncoderWorkletProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SAMPLES_PER_MS = 48;

export interface ProcessorOptions {
    timeSlice: number;
}

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor implements OpusEncoderWorklet {
    private static allowedTimeSlice = [20, 40, 60, 80];
    private readonly samplesPerWindow: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferDeque: Denque<ArrayBuffer>;
    private server: Disposable;
    private worker: OpusEncoderWorker & Disposable;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        debugLog?.log('ctor');
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
        this.server = rpcServer(`${LogScope}.server`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<OpusEncoderWorker>(`${LogScope}.worker`, workerPort, this);
    }

    public async onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> {
        this.bufferDeque.push(buffer);
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        timerQueue?.triggerExpired();
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
                    if (this.worker != null)
                        void this.worker.onEncoderWorkletSamples(audioArrayBuffer, rpcNoWait);
                    else
                        warnLog?.log('process: worklet port is still undefined!');
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
}

// @ts-expect-error - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('opus-encoder-worklet-processor', OpusEncoderWorkletProcessor);
