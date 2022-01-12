import Denque from 'denque';
import {
    DataDecoderMessage,
    DecoderMessage, EndDecoderMessage, InitCompletedDecoderWorkerMessage, InitDecoderMessage, StopDecoderMessage,
} from "./opus-decoder-worker-message";
import { OpusDecoder } from "./opus-decoder";


const decoderPool = new Denque<OpusDecoder>();
const decoderMap = new Map<string, OpusDecoder>();
const worker = self as unknown as Worker;

worker.onmessage = async (ev: MessageEvent<DecoderMessage>): Promise<void> => {
    try {
        const msg = ev.data;
        switch (msg.type) {
            case 'load':
                await onLoadDecoder();
                break;

            case 'init':
                await onInit(msg as InitDecoderMessage);
                break;

            case 'data':
                await onData(msg as DataDecoderMessage);
                break;

            case 'end':
                await onEnd(msg as EndDecoderMessage);
                break;

            case 'stop':
                await onStop(msg as StopDecoderMessage);
                break;
        }
    }
    catch (error) {
        // TODO(AK): implement centralized logging for client, like Sentry, etc.
        console.error(error);
    }
};

async function onLoadDecoder(): Promise<void> {
    for (let i = 0; i < 3; ++i) {
        decoderPool.push(await OpusDecoder.create(release));
    }
}

async function onInit(message: InitDecoderMessage): Promise<void> {
    const { playerId, buffer, offset, length, workletPort } = message;
    let decoder = decoderPool.shift();
    if (decoder == null) {
        decoder = await OpusDecoder.create(release);
    }
    await decoder.init(playerId, workletPort, buffer, offset, length);
    decoderMap.set(playerId, decoder);

    const initCompletedMessage: InitCompletedDecoderWorkerMessage = { type: 'initCompleted', playerId: playerId };
    worker.postMessage(initCompletedMessage);
}

async function onData(message: DataDecoderMessage): Promise<void> {
    const { playerId, buffer, offset, length } = message;
    const decoder = getDecoder(playerId);
    decoder.pushData(buffer, offset, length);
}

async function onEnd(message: EndDecoderMessage): Promise<void> {
    const { playerId } = message;
    const decoder = getDecoder(playerId);
    decoder.pushEndOfStream();
}

async function onStop(message: StopDecoderMessage): Promise<void> {
    const { playerId } = message;
    const decoder = decoderMap.get(playerId);
    if (decoder == null) {
        // already processed by handling 'endOfStream' at the decoder
        return;
    }
    await decoder.stop();
}

function release(decoder: OpusDecoder): void {
    decoderMap.delete(decoder.playerId);
    decoderPool.push(decoder);
}

function getDecoder(playerId: string): OpusDecoder {
    const decoder = decoderMap.get(playerId);
    if (decoder == null)
        throw new Error(`Can't find decoder for player=${playerId}`);
    return decoder;
}
