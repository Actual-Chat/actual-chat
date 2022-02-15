import Denque from 'denque';
import SoxrResampler, { SoxrDatatype, SoxrQuality } from 'wasm-audio-resampler';
import { adjustChangeEventsToSeconds, VoiceActivityChanged, VoiceActivityDetector } from './audio-vad';
import { VadMessage } from './audio-vad-worker-message';
import OnnxModel from './vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';
import { BufferVadWorkletMessage } from '../worklets/audio-vad-worklet-message';

const CHANNELS = 1;
const IN_RATE = 48000;
const OUT_RATE = 16000;

const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);
let workletPort: MessagePort = null;

let encoderPort: MessagePort = null;
let resampler: SoxrResampler = null;
let voiceDetector: VoiceActivityDetector = null;
let isVadRunning = false;

onmessage = (ev: MessageEvent<VadMessage>) => {
    const { type } = ev.data;

    switch (type) {
        case 'load-module':
            void onLoadModule(ev.ports[0], ev.ports[1]);
            break;
        case 'init-new-stream':
            void onInitNewStream();
            break;

        default:
            break;
    }
};

async function onLoadModule(workletMessagePort: MessagePort, encoderMessagePort: MessagePort): Promise<void> {
    workletPort = workletMessagePort;
    encoderPort = encoderMessagePort;
    workletPort.onmessage = onWorkletMessage;
    encoderPort.onmessage = onEncoderMessage;
    queue.clear();
    resampler = new SoxrResampler(
        CHANNELS,
        IN_RATE,
        OUT_RATE,
        inputDatatype,
        outputDatatype,
        SoxrQuality.SOXR_MQ,
    );
    await resampler.init(SoxrModule, { 'locateFile': () => SoxrWasm as string });
    voiceDetector = new VoiceActivityDetector(OnnxModel as URL);
    await voiceDetector.init();
}

async function onInitNewStream(): Promise<void> {
    // resample silence to clean up internal state
    const silence = new Uint8Array(768* 4);
    resampler.processChunk(silence, resampleBuffer);
}

const onWorkletMessage = (ev: MessageEvent<BufferVadWorkletMessage>) => {
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

        void processQueue();
    }
};

const onEncoderMessage = (ev: MessageEvent) => {
    // do nothing;
};

async function processQueue(): Promise<void> {
    if (queue.isEmpty()) {
        return;
    }

    if (isVadRunning) {
        return;
    }

    if (resampler == null) {
        return;
    }

    try {
        isVadRunning = true;

        const buffer = queue.shift();
        const dataToResample = new Uint8Array(buffer);
        const resampled = resampler.processChunk(dataToResample, resampleBuffer).buffer;

        const bufferMessage: BufferVadWorkletMessage = {
            type: 'buffer',
            buffer: buffer,
        }

        workletPort.postMessage(bufferMessage, [buffer]);

        const monoPcm = new Float32Array(resampled, 0, 512);
        const vadEvent = await voiceDetector.appendChunk(monoPcm);
        if (vadEvent) {
            const adjustedVadEvent = adjustChangeEventsToSeconds(vadEvent);
            sendResult(adjustedVadEvent);
        }

    } catch (error) {
        isVadRunning = false;
        throw error;
    } finally {
        isVadRunning = false;
    }


    void processQueue();
}


function sendResult(result: VoiceActivityChanged): void {
    encoderPort.postMessage(result);
}
