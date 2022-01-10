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
    const { command }: DecoderCommand = ev.data;
    switch (command) {
        case 'loadDecoder':
            decoderPool.push(await OpusDecoder.create(release));
            decoderPool.push(await OpusDecoder.create(release));
            decoderPool.push(await OpusDecoder.create(release));
            break;

        case 'init': {
            const {playerId, buffer, offset, length}: InitCommand = ev.data;
            const workletPort = ev.ports[0];
            let decoder = decoderPool.shift();
            if (decoder == null) {
                decoder = await OpusDecoder.create(release);
            }
            await decoder.init(playerId, workletPort, buffer, offset, length);
            decoderMap.set(playerId, decoder);

            const initCompletedMessage: DecoderWorkerMessage = {topic: 'initCompleted', playerId: playerId};
            worker.postMessage(initCompletedMessage);
            break;
        }

        case 'pushData': {
            const {playerId, buffer, offset, length}: PushDataCommand = ev.data;
            const decoder = decoderMap.get(playerId);
            if (decoder == null) {
                console.error(`pushData: can't find decoder for player=${playerId}`);
                break;
            }

            decoder.pushData(buffer, offset, length);
            break;
        }

        case 'endOfStream': {
            const {playerId}: EndOfStreamCommand = ev.data;
            const decoder = decoderMap.get(playerId);
            if (decoder == null) {
                console.error(`endOfStream: can't find decoder for player=${playerId}`);
                break;
            }

            decoder.pushEndOfStream();
            break;
        }

        case 'stop': {
            const {playerId}: StopCommand = ev.data;
            const decoder = decoderMap.get(playerId);
            if (decoder == null) {
                // already processed by handling 'endOfStream' at the decoder
                break;
            }

            await decoder.stop();
            break;
        }
    }

};

function release(decoder: OpusDecoder): void {
    decoderMap.delete(decoder.playerId);
    decoderPool.push(decoder);
}

let getTimestamp;
if (typeof performance === 'undefined' || typeof performance.now === 'undefined') {
    getTimestamp = Date.now;
} else {
    getTimestamp = performance.now.bind(performance);
}
