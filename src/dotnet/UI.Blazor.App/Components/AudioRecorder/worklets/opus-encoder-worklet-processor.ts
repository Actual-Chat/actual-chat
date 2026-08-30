// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import { AUDIO, AppConstants, initAppConstants } from 'app-constants';
import { Disposable } from 'disposable';
import { ObjectPool } from 'object-pool';
import { rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { timerQueue } from 'timerQueue';
import { AudioRingBuffer } from '../audio-ring-buffer';
import { AudioDiagnosticsState } from '../audio-recorder';
import { OpusEncoderWorklet } from './opus-encoder-worklet-contract';
import { OpusEncoderWorker } from '../workers/opus-encoder-worker-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
import { getLogs } from 'logging';

const { logScope, debugLog, infoLog, warnLog, errorLog } = getLogs('OpusEncoderWorkletProcessor');

export interface OpusEncoderProcessorOptions {
    timeSlice: number;
    sampleRate: number;
}

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor implements OpusEncoderWorklet {
    private static allowedTimeSlice = [20, 40, 60, 80];
    private readonly samplesPerWindow: number;
    private readonly sampleRate: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;

    private state: 'running' | 'ready' | 'inactive' | 'terminated' = 'inactive';
    private stateServer: RecorderStateServer & Disposable;
    private worker: OpusEncoderWorker & Disposable;
    private samplesSinceLastReport: number | null = null;
    private frameCount = 0;
    private lastFrameProcessedAt = 0;
    private promiseQueue: Promise<void> = Promise.resolve();

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        debugLog?.log('ctor');
        const { timeSlice, sampleRate } = options.processorOptions as OpusEncoderProcessorOptions;

        if (!OpusEncoderWorkletProcessor.allowedTimeSlice.some(val => val === timeSlice)) {
            const allowedTimeSliceJson = JSON.stringify(OpusEncoderWorkletProcessor.allowedTimeSlice);
            throw new Error(`OpusEncoderWorkletProcessor supports only ${ allowedTimeSliceJson } options as timeSlice argument.`);
        }

        this.sampleRate = sampleRate;
        this.samplesPerWindow = Math.ceil(timeSlice * sampleRate / 1000);
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(this.samplesPerWindow * 4)).expandTo(4);
        this.stateServer = rpcClientServer<RecorderStateServer>(`${logScope}.stateServer`, this.port, this);
    }

    public async init(appConstants: AppConstants, workerPort: MessagePort): Promise<void> {
        initAppConstants(appConstants);
        this.worker = rpcClientServer<OpusEncoderWorker>(`${logScope}.worker`, workerPort, this);
        this.state = 'ready';
        this.samplesSinceLastReport = null;
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
    }

    public async start(_noWait?: RpcNoWait): Promise<void> {
        this.state = 'running';
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
        this.buffer.reset();
    }

    public async terminate(_noWait?: RpcNoWait): Promise<void> {
        this.state = 'terminated';
        this.samplesSinceLastReport = null;
        this.frameCount = 0;
        this.lastFrameProcessedAt = 0;
        this.buffer.reset();
    }

    public async releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> {
        this.bufferPool.release(buffer);
    }

    // called for each 128 samples ~ 2.5ms
    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        // if (inputs[0].length)
        //     console.log("RECORD:", approximateGain(inputs[0][0]));
        if (this.frameCount++ > 100) {
            this.frameCount = 0;
            this.lastFrameProcessedAt = Date.now();
        }

        timerQueue?.triggerExpired();
        try {
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
            this.buffer.push(input);
            if (this.buffer.samplesAvailable >= this.samplesPerWindow) {
                const capturedAtMs = Date.now()
                    - this.buffer.samplesAvailable / this.sampleRate * 1000;
                const audioArrayBuffer = this.bufferPool.get();
                const audioArray = new Float32Array(audioArrayBuffer, 0, this.samplesPerWindow)

                if (this.buffer.pull([audioArray])) {
                    if (this.worker != null)
                        // The catch keeps the chain alive: one rejection - a DataCloneError on a
                        // detached pooled buffer, a disposed proxy - leaves promiseQueue
                        // permanently rejected, so every later .then is skipped and mic samples
                        // stop reaching the encoder for good, with nothing but an unhandled
                        // rejection to show for it.
                        this.promiseQueue = this.promiseQueue
                            .then(() => this.worker.onEncoderWorkletSamples(audioArrayBuffer, capturedAtMs, rpcNoWait))
                            .catch((e: unknown) => warnLog?.log('process: failed to hand samples to the worker', e));
                    else
                        warnLog?.log('process: worklet port is still undefined!');
                } else {
                    this.bufferPool.release(audioArrayBuffer);
                }
            }

            this.samplesSinceLastReport ??= AUDIO.rec.recordingInProgressReportSamples;
            this.samplesSinceLastReport += input[0].length;
            if (this.samplesSinceLastReport >= AUDIO.rec.recordingInProgressReportSamples) {
                this.samplesSinceLastReport = 0;
                void this.stateServer.microphoneIsCaptured(rpcNoWait);
            }
        }
        catch (error) {
            errorLog?.log(`process: unhandled error:`, error);
        }

        return true;
    }

    public async runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> {
        diagnosticsState.encoderWorkletState = this.state;
        diagnosticsState.lastEncoderWorkletFrameProcessedAt = this.lastFrameProcessedAt;
        infoLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    }
}

registerProcessor('opus-encoder-worklet-processor', OpusEncoderWorkletProcessor);
