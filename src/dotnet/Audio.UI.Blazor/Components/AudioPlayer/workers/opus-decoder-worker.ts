import Denque from 'denque';
import {
    DecoderCommand,
    DecoderWorkerMessage,
    EndOfStreamCommand,
    InitCommand,
    PushDataCommand, StopCommand
} from "./opus-decoder-worker-message";
import { OpusDecoder } from "./opus-decoder";


const decoderPool = new Denque<OpusDecoder>();
const decoderMap = new Map<string,OpusDecoder>();
const worker = self as unknown as Worker;

worker.onmessage = async (ev: MessageEvent) => {
    try {
        const {command}: DecoderCommand = ev.data;
        switch (command) {
            case 'loadDecoder':
                await onLoadDecoder();
                break;

            case 'init':
                await onInit(ev.data, ev.ports[0]);
                break;

            case 'pushData':
                await onPushData(ev.data);
                break;

            case 'endOfStream':
                await onEndOfStream(ev.data);
                break;

            case 'stop':
                await onStop(ev.data);
                break;
        }
    }
    catch (error) {
        // TODO(AK): implement centralized logging for client, like Sentry, etc.
        console.error(error);
    }
};

async function onLoadDecoder(): Promise<void> {
    decoderPool.push(await OpusDecoder.create(release));
    decoderPool.push(await OpusDecoder.create(release));
    decoderPool.push(await OpusDecoder.create(release));
}

async function onInit(command: InitCommand, port: MessagePort): Promise<void> {
    const { playerId, buffer, offset, length } = command;
    const workletPort = port;
    let decoder = decoderPool.shift();
    if (decoder == null) {
        decoder = await OpusDecoder.create(release);
    }
    await decoder.init(playerId, workletPort, buffer, offset, length);
    decoderMap.set(playerId, decoder);

    const initCompletedMessage: DecoderWorkerMessage = {topic: 'initCompleted', playerId: playerId};
    worker.postMessage(initCompletedMessage);
}

async function onPushData(command: PushDataCommand): Promise<void> {
    const { playerId, buffer, offset, length } = command;
    const decoder = getDecoder(playerId);
    decoder.pushData(buffer, offset, length);
}

async function onEndOfStream(command: EndOfStreamCommand): Promise<void> {
    const {playerId} = command;
    const decoder = getDecoder(playerId);
    decoder.pushEndOfStream();
}

async function onStop(command: StopCommand): Promise<void> {
    const {playerId} = command;
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
