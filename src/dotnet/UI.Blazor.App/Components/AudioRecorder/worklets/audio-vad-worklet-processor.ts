// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import { AppConstants, initAppConstants } from 'app-constants';
import { Disposable } from 'disposable';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import { timerQueue } from 'timerQueue';
import { ObjectPool } from 'object-pool';
import { AudioRingBuffer } from '../audio-ring-buffer';
import { AudioVadWorker } from '../workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './audio-vad-worklet-contract';
import { AudioDiagnosticsState } from '../audio-recorder';
import { getLogs } from 'logging';

const { logScope, infoLog, warnLog } = getLogs('AudioVadWorkletProcessor');

export interface AudioVadProcessorOptions {
    sampleRate: number;
}

export class AudioVadWorkletProcessor extends AudioWorkletProcessor implements AudioVadWorklet {
    private readonly buffer: AudioRingBuffer;
    private readonly sampleRate: number;

    private state: 'running' | 'ready' | 'inactive' | 'terminated' = 'inactive';
    private samplesPerWindow = 0; // overwritten by start(windowSizeMs)
    private bufferPool: ObjectPool<ArrayBuffer>;
    private server: Disposable;
    private worker: AudioVadWorker & Disposable;
    private frameCount = 0;
    private lastFrameProcessedAt = 0;
    private promiseQueue: Promise<void> = Promise.resolve();

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        const { sampleRate } = options.processorOptions as AudioVadProcessorOptions;
        this.sampleRate = sampleRate;
        this.buffer = new AudioRingBuffer(8192, 1);
        this.server = rpcServer(`${logScope}.server`, this.port, this);
    }

    public async init(appConstants: AppConstants, workerPort: MessagePort): Promise<void> {
        initAppConstants(appConstants);
        this.worker = rpcClientServer<AudioVadWorker>(`${logScope}.worker`, workerPort, this);
        this.state = 'ready';
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
    }

    public async start(windowSizeMs: 30 | 32): Promise<void> {
        this.samplesPerWindow = Math.ceil(windowSizeMs == 30
            ? 30 * this.sampleRate / 1000
            : 32 * this.sampleRate / 1000);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(this.samplesPerWindow * 4)).expandTo(4);
        this.state = 'running';
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
        this.buffer.reset();
    }

    public async terminate(_noWait?: RpcNoWait): Promise<void> {
        this.state = 'terminated';
    }

    public async releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> {
        // we can change samplesPerWindow on the fly when switching to NN VAD
        if (buffer.byteLength !== this.samplesPerWindow * 4)
            return;

        this.bufferPool.release(buffer);
    }

    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        // debugLog?.log(`process:`, this.state);
        // if (inputs[0].length)
        //     console.log("VAD:", approximateGain(inputs[0][0]));
        if (this.frameCount++ > 100) {
            this.frameCount = 0;
            this.lastFrameProcessedAt = Date.now();
        }
        timerQueue?.triggerExpired();
        const hasInput = inputs
            && inputs.length !== 0
            && inputs[0].length !== 0;

        if (this.state === 'terminated')
            return false;

        if (this.state === 'inactive')
            return true;

        if (!hasInput)
            return true;

        const input = inputs[0];
        const { samplesPerWindow } = this;

        this.buffer.push(input);
        if (this.buffer.samplesAvailable >= samplesPerWindow) {
            const vadArrayBuffer = this.bufferPool.get();
            const vadArray = new Float32Array(vadArrayBuffer, 0, samplesPerWindow);

            if (this.buffer.pull([vadArray])) {
                if (this.worker)
                    // See the same guard in opus-encoder-worklet-processor: without it one
                    // rejection poisons the chain and the VAD stops receiving frames for good.
                    this.promiseQueue = this.promiseQueue
                        .then(() => this.worker.onFrame(vadArrayBuffer, rpcNoWait))
                        .catch((e: unknown) => warnLog?.log('process: failed to hand a frame to the worker', e));
                else
                    warnLog?.log('process: worklet port is still undefined!');
            } else {
                this.bufferPool.release(vadArrayBuffer);
            }
        }

        return true;
    }

    public async runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> {
        diagnosticsState.vadWorkletState = this.state;
        diagnosticsState.lastVadWorkletFrameProcessedAt = this.lastFrameProcessedAt;
        infoLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    }
}

registerProcessor('audio-vad-worklet-processor', AudioVadWorkletProcessor);
