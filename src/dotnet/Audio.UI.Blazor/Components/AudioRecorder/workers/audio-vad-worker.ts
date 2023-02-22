import Denque from 'denque';
import { Disposable } from 'disposable';
import SoxrResampler, { SoxrDatatype, SoxrQuality } from 'wasm-audio-resampler';
import { adjustChangeEventsToSeconds, VoiceActivityDetector } from './audio-vad';
import { AudioVadWorker } from './audio-vad-worker-contract';
import OnnxModel from './vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';
import { rpcClientServer, rpcServer } from 'rpc';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { AudioVadWorklet } from '../worklets/audio-vad-worklet-contract';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioVadWorker';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const CHANNELS = 1;
const IN_RATE = 48000;
const OUT_RATE = 16000;

const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);

let vadWorklet: AudioVadWorklet & Disposable = null;
let encoderWorker: OpusEncoderWorker & Disposable = null;
let resampler: SoxrResampler = null;
let voiceDetector: VoiceActivityDetector = null;
let isVadRunning = false;
let isActive = false;

const serverImpl: AudioVadWorker = {
    create: async (): Promise<void> => {
        if (vadWorklet != null || encoderWorker != null)
            throw 'Already initialized.';
        debugLog?.log(`-> onCreate`);

        queue.clear();
        resampler = new SoxrResampler(
            CHANNELS,
            IN_RATE,
            OUT_RATE,
            inputDatatype,
            outputDatatype,
            SoxrQuality.SOXR_MQ,
        );
        await resampler.init(SoxrModule, { 'locateFile': () => SoxrWasm });
        voiceDetector = new VoiceActivityDetector(OnnxModel as unknown as URL);
        await voiceDetector.init();
        debugLog?.log(`<- onCreate`);
    },

    init: async (workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void> => {
        vadWorklet = rpcClientServer<AudioVadWorklet>(workletPort, this);
        encoderWorker = rpcClientServer<OpusEncoderWorker>(encoderWorkerPort, this);
        isActive = true;
    },

    reset: async (): Promise<void> => {
        // it is safe to skip init while it still not active
        if (!isActive)
            return;

        // resample silence to clean up internal isActive
        const silence = new Uint8Array(768 * 4);
        resampler.processChunk(silence, resampleBuffer);
        voiceDetector.reset();
    },

    append: async (buffer: ArrayBuffer): Promise<void> => {
        if (!isActive)
            return;

        if (buffer && buffer.byteLength !== 0) {
            queue.push(buffer);
            await processQueue();
        }
    },
};
const server = rpcServer(globalThis as unknown as Worker, serverImpl);

async function processQueue(): Promise<void> {
    if (isVadRunning || resampler == null)
        return;

    try {
        isVadRunning = true;
        while (true) {
            if (queue.isEmpty()) {
                return;
            }

            const buffer = queue.shift();
            const dataToResample = new Uint8Array(buffer);
            const resampled = resampler.processChunk(dataToResample, resampleBuffer).buffer;
            void vadWorklet.append(buffer);

            const monoPcm = new Float32Array(resampled, 0, 512);
            const vadEvent = await voiceDetector.appendChunk(monoPcm);
            if (vadEvent) {
                const adjustedVadEvent = adjustChangeEventsToSeconds(vadEvent);
                void encoderWorker.onVoiceActivityChange(adjustedVadEvent);
            }
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    } finally {
        isVadRunning = false;
    }
}
