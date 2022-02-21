import { CreateDecoderMessage, DataDecoderMessage, DecoderMessage, EndDecoderMessage, InitDecoderMessage, OperationCompletedDecoderWorkerMessage, StopDecoderMessage } from './opus-decoder-worker-message';
import { OpusDecoder } from './opus-decoder';

const worker = self as unknown as Worker;
const decoders = new Map<number, OpusDecoder>();
const debug = false;
const debugPushes: boolean = debug && true;

worker.onmessage = async (ev: MessageEvent<DecoderMessage>): Promise<void> => {
    try {
        const msg = ev.data;
        switch (msg.type) {
            case 'create':
                await onCreate(msg as CreateDecoderMessage);
                break;
            case 'init':
                await onInit(msg as InitDecoderMessage);
                break;
            case 'data':
                onData(msg as DataDecoderMessage);
                break;
            case 'end':
                onEnd(msg as EndDecoderMessage);
                break;
            case 'stop':
                onStop(msg as StopDecoderMessage);
                break;
            default:
                throw new Error(`Decoder Worker: Unsupported DecoderMessage type: ${msg.type}`);
        }
    }
    catch (error) {
        console.error(error);
    }
};

function getDecoder(controllerId: number): OpusDecoder {
    const decoder = decoders.get(controllerId);
    if (decoder === undefined) {
        throw new Error(`Can't find decoder object for controllerId:${controllerId}`);
    }
    return decoder;
}

async function onCreate(message: CreateDecoderMessage) {
    const { callbackId, workletPort, controllerId } = message;
    // decoders are pooled with the parent object, so we don't need an object pool here
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): start create`);
    const decoder = await OpusDecoder.create(workletPort);
    decoders.set(controllerId, decoder);
    const msg: OperationCompletedDecoderWorkerMessage = {
        type: 'operationCompleted',
        callbackId: callbackId,
    };
    worker.postMessage(msg);
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): end create`);
}

async function onInit(message: InitDecoderMessage): Promise<void> {
    const { callbackId, controllerId, buffer, length, offset } = message;
    const decoder = getDecoder(controllerId);
    const data = buffer.slice(offset, offset + length);
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): start init, header - ${data.byteLength} bytes`);
    await decoder.init(data);

    const msg: OperationCompletedDecoderWorkerMessage = {
        type: 'operationCompleted',
        callbackId: callbackId,
    };
    worker.postMessage(msg);
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): end init`);
}

function onData(message: DataDecoderMessage): void {
    const { controllerId, buffer, offset, length } = message;
    const decoder = getDecoder(controllerId);
    const data = buffer.slice(offset, offset + length);
    if (debugPushes)
        console.debug(`Decoder(controllerId:${controllerId}): push ${data.byteLength} data bytes`);
    decoder.pushData(data);
}

function onEnd(message: EndDecoderMessage): void {
    const { controllerId } = message;
    const decoder = getDecoder(controllerId);
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): push end`);

    decoder.pushEnd();
}

function onStop(message: StopDecoderMessage): void {
    const { controllerId } = message;
    const decoder = getDecoder(controllerId);
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): start stop`);
    decoder.stop();
    if (debug)
        console.debug(`Decoder(controllerId:${controllerId}): end stop`);
}

self['getDecoder'] = getDecoder;