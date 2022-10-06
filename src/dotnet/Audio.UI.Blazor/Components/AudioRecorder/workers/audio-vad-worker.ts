import Denque from 'denque';
import SoxrResampler, { SoxrDatatype, SoxrQuality } from 'wasm-audio-resampler';
import { adjustChangeEventsToSeconds, VoiceActivityDetector } from './audio-vad';
import { VadMessage } from './audio-vad-worker-message';
import OnnxModel from './vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';
import { BufferVadWorkletMessage } from '../worklets/audio-vad-worklet-message';

const LogScope: string = 'AudioVadWorker';

const CHANNELS = 1;
const IN_RATE = 48000;
const OUT_RATE = 16000;

const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);

let workletPort: MessagePort = null;
let isActive: boolean = false;
let encoderPort: MessagePort = null;
let resampler: SoxrResampler = null;
let voiceDetector: VoiceActivityDetector = null;
let isVadRunning = false;

onmessage = async (ev: MessageEvent<VadMessage>) => {
    try {
        const { type } = ev.data;

        switch (type) {
        case 'create':
            await onCreate(ev.ports[0], ev.ports[1]);
            break;
        case 'reset':
            onReset();
            break;
        default:
            throw new Error(`Unsupported message type: ${type as string}`);
        }
    } catch (error) {
        console.error(`${LogScope}.onmessage error:`, error);
    }
};

async function onCreate(workletMessagePort: MessagePort, encoderMessagePort: MessagePort): Promise<void> {
    if (workletPort != null) {
        throw new Error('workletPort has already been specified.');
    }
    if (encoderPort != null) {
        throw new Error('encoderPort has already been specified.');
    }

    workletPort = workletMessagePort;
    encoderPort = encoderMessagePort;
    workletPort.onmessage = onWorkletMessage;
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
    isActive = true;
}

function onReset(): void {
    // it is safe to skip init while it still not active
    if (!isActive)
        return;

    // resample silence to clean up internal isActive
    const silence = new Uint8Array(768 * 4);
    resampler.processChunk(silence, resampleBuffer);
    voiceDetector.reset();
}

const onWorkletMessage = async (ev: MessageEvent<BufferVadWorkletMessage>) => {
    if (!isActive)
        return;

    try {
        const { type, buffer } = ev.data;

        let vadBuffer: ArrayBuffer;
        switch (type) {
        case 'buffer':
            vadBuffer = buffer;
            break;
        default:
            break;
        }
        if (vadBuffer && vadBuffer.byteLength !== 0) {
            queue.push(buffer);
            await processQueue();
        }
    } catch (error) {
        console.error(`${LogScope}.onWorkletMessage error:`, error);
    }
};

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

            const bufferMessage: BufferVadWorkletMessage = {
                type: 'buffer',
                buffer: buffer,
            };

            workletPort.postMessage(bufferMessage, [buffer]);

            const monoPcm = new Float32Array(resampled, 0, 512);
            const vadEvent = await voiceDetector.appendChunk(monoPcm);
            if (vadEvent) {
                const adjustedVadEvent = adjustChangeEventsToSeconds(vadEvent);
                encoderPort.postMessage(adjustedVadEvent);
            }
        }
    }
    catch (error) {
        console.error(`${LogScope}.processQueue error:`, error);
    } finally {
        isVadRunning = false;
    }
}
