import { AudioRingBuffer } from './audio-ring-buffer';
import { AudioVadWorker } from '../workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './audio-vad-worklet-contract';
import { Disposable } from 'disposable';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import { timerQueue } from 'timerQueue';
import { ObjectPool } from 'object-pool';
import { Log } from 'logging';

const { logScope, warnLog } = Log.get('AudioVadWorkletProcessor');

const SAMPLES_PER_WINDOW = 768;

export class AudioVadWorkletProcessor extends AudioWorkletProcessor implements AudioVadWorklet {
    private readonly buffer: AudioRingBuffer;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;

    private state: 'running' | 'stopped' = 'running';
    private server: Disposable;
    private worker: AudioVadWorker & Disposable;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(SAMPLES_PER_WINDOW * 4)).expandTo(4);
        this.server = rpcServer(`${logScope}.server`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<AudioVadWorker>(`${logScope}.worker`, workerPort, this);
    }

    public async stop(_noWait?: RpcNoWait): Promise<void> {
        this.state = 'stopped';
    }

    public async releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> {
        this.bufferPool.release(buffer);
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        timerQueue?.triggerExpired();
        const hasInput = inputs
            && inputs.length !== 0
            && inputs[0].length !== 0;
        const hasOutput = outputs
            && outputs.length !== 0
            && outputs[0].length !== 0;

        if (this.state === 'stopped')
            return false;
        if (!hasInput || !hasOutput)
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
            const vadArrayBuffer = this.bufferPool.get();

            vadBuffer.push(new Float32Array(vadArrayBuffer, 0, SAMPLES_PER_WINDOW));

            if (this.buffer.pull(vadBuffer)) {
                if (this.worker)
                    void this.worker.onFrame(vadArrayBuffer, rpcNoWait);
                else
                    warnLog?.log('process: worklet port is still undefined!');
            } else {
                this.bufferPool.release(vadArrayBuffer);
            }
        }

        return true;
    }
}

// @ts-expect-error - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('audio-vad-worklet-processor', AudioVadWorkletProcessor);
