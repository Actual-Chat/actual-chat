import Denque from 'denque';
import { adjustChangeEventsToSeconds, VoiceActivityChanged, VoiceActivityDetector } from "../audio-vad";
import { VadMessage } from "../audio-vad-message";
import OnnxModel from '../vad.onnx';

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
            queue.clear();
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

    try {
        isVadRunning = true;

        const buffer = queue.pop();
        const monoPcm = new Float32Array(buffer);
        const vadEvent = await voiceDetector.appendChunk(monoPcm);
        if (vadEvent) {
            const adjustedVadEvent = adjustChangeEventsToSeconds(vadEvent);
            sendResult(adjustedVadEvent);
        }
        workletPort.postMessage({ topic: "buffer", buffer: buffer }, [buffer]);

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
