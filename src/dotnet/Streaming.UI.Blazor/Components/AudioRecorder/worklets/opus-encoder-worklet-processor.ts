import { AudioRingBuffer } from './audio-ring-buffer';
import { Disposable } from 'disposable';
import { ObjectPool } from 'object-pool';
import { OpusEncoderWorklet } from './opus-encoder-worklet-contract';
import { OpusEncoderWorker } from '../workers/opus-encoder-worker-contract';
import { rpcClientServer, RpcNoWait, rpcNoWait, rpcServer } from 'rpc';
import { timerQueue } from 'timerQueue';
import { Log } from 'logging';
import { RecorderStateEventHandler } from "../opus-media-recorder-contracts";
import {AudioDiagnosticsState} from "../audio-recorder";

const { logScope, debugLog, warnLog, errorLog } = Log.get('OpusEncoderWorkletProcessor');

const SAMPLES_PER_MS = 48;
const SAMPLES_PER_RECORDING_REPORT_CALL = 9600; // 200ms

export interface ProcessorOptions {
    timeSlice: number;
}

export class OpusEncoderWorkletProcessor extends AudioWorkletProcessor implements OpusEncoderWorklet {
    private static allowedTimeSlice = [20, 40, 60, 80];
    private readonly samplesPerWindow: number;
    private readonly buffer: AudioRingBuffer;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;
    private state: 'running' | 'stopped' | 'inactive' = 'inactive';
    private server: RecorderStateEventHandler & Disposable;
    private worker: OpusEncoderWorker & Disposable;
    private samplesSinceLastReporting = 0;
    private frameCount: number = 0;
    private lastFrameProcessedAt: number = 0;

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
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(this.samplesPerWindow * 4)).expandTo(4);
        this.server = rpcClientServer<RecorderStateEventHandler>(`${logScope}.server`, this.port, this);
    }

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<OpusEncoderWorker>(`${logScope}.worker`, workerPort, this);
        this.state = 'running';
    }

    public async stop(_noWait?: RpcNoWait): Promise<void> {
        this.state = 'stopped';
    }

    public async releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> {
        this.bufferPool.release(buffer);
    }

    // called for each 128 samples ~ 2.5ms
    public process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
        if (this.frameCount++ > 100) {
            this.frameCount = 0;
            this.lastFrameProcessedAt = Date.now();
        }

        timerQueue?.triggerExpired();
        try {
            const hasInput = inputs
                && inputs.length !== 0
                && inputs[0].length !== 0;

            if (this.state === 'stopped')
                return false;
            if (this.state === 'inactive')
                return true;
            if (!hasInput)
                return true;

            const input = inputs[0];
            this.buffer.push(input);
            if (this.buffer.framesAvailable >= this.samplesPerWindow) {
                const audioBuffer = new Array<Float32Array>();
                const audioArrayBuffer = this.bufferPool.get();

                audioBuffer.push(new Float32Array(audioArrayBuffer, 0, this.samplesPerWindow));

                if (this.buffer.pull(audioBuffer)) {
                    if (this.worker != null)
                        void this.worker.onEncoderWorkletSamples(audioArrayBuffer, rpcNoWait);
                    else
                        warnLog?.log('process: worklet port is still undefined!');
                } else {
                    this.bufferPool.release(audioArrayBuffer);
                }
            }

            this.samplesSinceLastReporting += input[0].length;
            if (this.samplesSinceLastReporting > SAMPLES_PER_RECORDING_REPORT_CALL) {
                this.samplesSinceLastReporting = 0;
                void this.server.recordingInProgress(rpcNoWait);
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
