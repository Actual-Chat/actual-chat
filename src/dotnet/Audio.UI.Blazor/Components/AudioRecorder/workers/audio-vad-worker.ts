import Denque from 'denque';
import SoxrResampler, {SoxrDatatype, SoxrQuality} from 'wasm-audio-resampler';
import { adjustChangeEventsToSeconds, VoiceActivityChanged, VoiceActivityDetector } from "../audio-vad";
import { VadMessage } from "../audio-vad-message";
import OnnxModel from '../vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';


const CHANNELS = 1;
const IN_RATE = 48000;
const OUT_RATE = 16000;

const voiceDetector = new VoiceActivityDetector(OnnxModel);
const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);

let workletPort: MessagePort = null;
let resampler: SoxrResampler = null;
let isVadRunning: boolean = false;

onmessage = async (ev) => {
    const { topic }: VadMessage = ev.data;

    switch (topic) {
        case 'init-port':
            workletPort = ev.ports[0];
            workletPort.onmessage = onWorkletMessage;
            queue.clear();
            break;
        case 'init-new-stream':
            const newResampler = new SoxrResampler(
                CHANNELS,
                IN_RATE,
                OUT_RATE,
                inputDatatype,
                outputDatatype,
                SoxrQuality.SOXR_MQ
            );
            await newResampler.init(SoxrModule, { 'locateFile': (path:string, directory: string) => SoxrWasm });
            resampler = newResampler;
            break;

        default:
            break;

    }
};

const onWorkletMessage = async (ev: MessageEvent<VadMessage>) => {
    const { topic, buffer }: VadMessage = ev.data;

    let vadBuffer: ArrayBuffer;
    switch (topic) {
        case 'buffer':
            vadBuffer = buffer;
            break;
        default:
            break;
    }
    if (vadBuffer.byteLength !== 0) {
        queue.push(buffer);

        const _ = processQueue();
    }
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

        workletPort.postMessage({ topic: "buffer", buffer: buffer }, [buffer]);

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


    const _ = processQueue();
}


function sendResult(result: VoiceActivityChanged): void {
    postMessage(result);
}
