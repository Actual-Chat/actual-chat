import { AUDIO_REC as AR } from '_constants';
import { Disposable } from 'disposable';
import { ObjectPool } from 'object-pool';
import { rpcClientServer, RpcNoWait, rpcNoWait, rpcServer } from 'rpc';
import { timerQueue } from 'timerQueue';
import { AudioRingBuffer } from './audio-ring-buffer';
import { AudioDiagnosticsState } from "../audio-recorder";
import { OpusEncoderWorklet } from './opus-encoder-worklet-contract';
import { OpusEncoderWorker } from '../workers/opus-encoder-worker-contract';
import { RecorderStateServer } from "../opus-media-recorder-contracts";
import { Log } from 'logging';
import { approximateGain } from 'math';

const { logScope, debugLog, warnLog, errorLog } = Log.get('OpusEncoderWorkletProcessor');

export interface ProcessorOptions {
    timeSlice: number;
}

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor implements OpusEncoderWorklet {
    private static allowedTimeSlice = [20, 40, 60, 80];
    private readonly samplesPerWindow: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;

    private state: 'running' | 'ready' | 'inactive' | 'terminated' = 'inactive';
    private stateServer: RecorderStateServer & Disposable;
    private worker: OpusEncoderWorker & Disposable;
    private samplesSinceLastReport: number = null;
    private frameCount: number = 0;
    private lastFrameProcessedAt: number = 0;
    private promiseQueue: Promise<void> = Promise.resolve();

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        debugLog?.log('ctor');
        const { timeSlice } = options.processorOptions as ProcessorOptions;

        if (!OpusEncoderWorkletProcessor.allowedTimeSlice.some(val => val === timeSlice)) {
            const allowedTimeSliceJson = JSON.stringify(OpusEncoderWorkletProcessor.allowedTimeSlice);
            throw new Error(`OpusEncoderWorkletProcessor supports only ${ allowedTimeSliceJson } options as timeSlice argument.`);
        }

        this.samplesPerWindow = timeSlice * AR.SAMPLES_PER_MS;
        this.buffer = new AudioRingBuffer(8192, 1);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(this.samplesPerWindow * 4)).expandTo(4);
        this.stateServer = rpcClientServer<RecorderStateServer>(`${logScope}.stateServer`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
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
                const audioBuffer = new Array<Float32Array>();
                const audioArrayBuffer = this.bufferPool.get();

                audioBuffer.push(new Float32Array(audioArrayBuffer, 0, this.samplesPerWindow));

                if (this.buffer.pull(audioBuffer)) {
                    if (this.worker != null)
                        this.promiseQueue = this.promiseQueue.then(() =>
                            this.worker.onEncoderWorkletSamples(audioArrayBuffer, rpcNoWait));
                    else
                        warnLog?.log('process: worklet port is still undefined!');
                } else {
                    this.bufferPool.release(audioArrayBuffer);
                }
            }

            this.samplesSinceLastReport ??= AR.SAMPLES_PER_RECORDING_IN_PROGRESS_CALL;
            this.samplesSinceLastReport += input[0].length;
            if (this.samplesSinceLastReport >= AR.SAMPLES_PER_RECORDING_IN_PROGRESS_CALL) {
                this.samplesSinceLastReport = 0;
                void this.stateServer.recordingInProgress(rpcNoWait);
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
        warnLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    }
}

registerProcessor('opus-encoder-worklet-processor', OpusEncoderWorkletProcessor);
