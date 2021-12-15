import Denque from 'denque';
import { adjustChangeEventsToSeconds, VoiceActivityChanged, VoiceActivityDetector } from "../audio-vad";
import { VadMessage } from "../audio-vad.message";
import OnnxModel from '../vad-model-mini.onnx';

const voiceDetector = new VoiceActivityDetector(OnnxModel);
const queue = new Denque<ArrayBuffer>();

let workletPort: MessagePort = null;
let isVadRunning: boolean = false;

onmessage = (ev) => {
    const { topic }: VadMessage = ev.data;

    switch (topic) {
        case 'init-port':
            workletPort = ev.ports[0];
            workletPort.onmessage = onWorkletMessage;
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
        default:
            break;
    }
    if (vadBuffer.byteLength !== 0) {
        queue.push(buffer);

        processQueue();
    }
};

async function processQueue(): Promise<void> {
    if (queue.isEmpty()) {
        return;
    }

    if (isVadRunning) {
        return;
    }

    try {
        isVadRunning = true;

        const buffer = queue.pop();
        const monoPcm = new Float32Array(buffer);
        const vadEvents = await voiceDetector.appendChunk(monoPcm);
        if (vadEvents.length) {
            const adjustedVadEvents = adjustChangeEventsToSeconds(vadEvents);
            sendResult(adjustedVadEvents[adjustedVadEvents.length - 1]);
        }
        workletPort.postMessage({ topic: "buffer", buffer: buffer }, [buffer]);

    } catch (error) {
        isVadRunning = false;
        throw error;
    } finally {
        isVadRunning = false;
    }


    processQueue();
}


function sendResult(result: VoiceActivityChanged): void {
    postMessage(result);
}
