import { AUDIO_REC as AR } from '_constants';
import { Disposable } from 'disposable';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import { timerQueue } from 'timerQueue';
import { ObjectPool } from 'object-pool';
import { AudioRingBuffer } from './audio-ring-buffer';
import { AudioVadWorker } from '../workers/audio-vad-worker-contract';
import { AudioVadWorklet } from './audio-vad-worklet-contract';
import { AudioDiagnosticsState } from "../audio-recorder";
import { Log } from 'logging';
import { approximateGain } from 'math';

const { logScope, debugLog, warnLog } = Log.get('AudioVadWorkletProcessor');

export class AudioVadWorkletProcessor extends AudioWorkletProcessor implements AudioVadWorklet {
    private readonly buffer: AudioRingBuffer;

    private state: 'running' | 'ready' | 'inactive' | 'terminated' = 'inactive';
    private samplesPerWindow: number = AR.SAMPLES_PER_WINDOW_32;
    private bufferPool: ObjectPool<ArrayBuffer>;
    private server: Disposable;
    private worker: AudioVadWorker & Disposable;
    private frameCount: number = 0;
    private lastFrameProcessedAt: number = 0;
    private promiseQueue: Promise<void> = Promise.resolve();

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.buffer = new AudioRingBuffer(8192, 1);
        this.server = rpcServer(`${logScope}.server`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<AudioVadWorker>(`${logScope}.worker`, workerPort, this);
        this.state = 'ready';
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
    }

    public async start(windowSizeMs: 30 | 32): Promise<void> {
        this.samplesPerWindow = windowSizeMs == 30
           ? AR.SAMPLES_PER_WINDOW_30
           : AR.SAMPLES_PER_WINDOW_32;
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
                    this.promiseQueue = this.promiseQueue.then(() =>
                        this.worker.onFrame(vadArrayBuffer, rpcNoWait));
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
        warnLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    }
}

registerProcessor('audio-vad-worklet-processor', AudioVadWorkletProcessor);
