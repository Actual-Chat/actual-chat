import Denque from 'denque';
import SoxrResampler, { SoxrDatatype, SoxrQuality } from 'wasm-audio-resampler';
import { adjustChangeEventsToSeconds, VoiceActivityChanged, VoiceActivityDetector } from './audio-vad';
import { VadMessage } from './audio-vad-worker-message';
import OnnxModel from './vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';

const CHANNELS = 1;
const IN_RATE = 48000;
const OUT_RATE = 16000;

const voiceDetector = new VoiceActivityDetector(OnnxModel as URL);
const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);

let workletPort: MessagePort = null;
let encoderPort: MessagePort = null;
let resampler: SoxrResampler = null;
let isVadRunning = false;

onmessage = (ev: MessageEvent<VadMessage>) => {
    const { type } = ev.data;

    switch (type) {
        case 'init-port':
            onInitPort(ev.ports[0], ev.ports[1]);
            break;
        case 'init-new-stream':
            void onInitNewStream();
            break;

        default:
            break;
    }
};

function onInitPort(workletMessagePort: MessagePort, encoderMessagePort: MessagePort) {
    workletPort = workletMessagePort;
    encoderPort = encoderMessagePort;
    workletPort.onmessage = onWorkletMessage;
    encoderPort.onmessage = onEncoderMessage;
    queue.clear();
}

async function onInitNewStream(): Promise<void> {
    const newResampler = new SoxrResampler(
        CHANNELS,
        IN_RATE,
        OUT_RATE,
        inputDatatype,
        outputDatatype,
        SoxrQuality.SOXR_LQ
    );
    await newResampler.init(SoxrModule, { 'locateFile': (path:string, directory: string) => SoxrWasm as string });
    resampler = newResampler;
}

const onWorkletMessage = (ev: MessageEvent<VadMessage>) => {
    const { type, buffer }: VadMessage = ev.data;

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

        const buffer = queue.pop();
        const dataToResample = new Uint8Array(buffer);
        const resampled = resampler.processChunk(dataToResample, resampleBuffer).buffer;

        const bufferMessage: VadMessage = {
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
