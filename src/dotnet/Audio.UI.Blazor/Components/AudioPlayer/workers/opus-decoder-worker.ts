// Commented out because it causes ts compilation issues in webpack release mode
// /// <reference lib="WebWorker" />
// export type { };
// declare const self: WorkerGlobalScope;

import { CreateDecoderMessage, DataDecoderMessage, DecoderMessage, EndDecoderMessage, InitDecoderMessage, OperationCompletedDecoderWorkerMessage, StopDecoderMessage } from './opus-decoder-worker-message';
import { OpusDecoder } from './opus-decoder';
import { Log, LogLevel } from 'logging-abstractions';

const LogScope: string = 'OpusDecoderWorker'
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);
const debug = debugLog != null;
const debugOnData: boolean = debug && false;

const worker = self as unknown as Worker;
const decoders = new Map<number, OpusDecoder>();

worker.onmessage = async (ev: MessageEvent<DecoderMessage>): Promise<void> => {
    try {
        const msg = ev.data;
        switch (msg.type) {
        case 'create':
            await onCreate(msg as CreateDecoderMessage);
            break;
        case 'init':
            onInit(msg as InitDecoderMessage);
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
            throw new Error(`Unsupported message type: ${msg.type}`);
        }
    }
    catch (error) {
        errorLog?.log(`worker.onmessage error:`, error);
    }
};

function getDecoder(controllerId: number): OpusDecoder {
    const decoder = decoders.get(controllerId);
    if (decoder === undefined) {
        throw new Error(`Can't find decoder object for controller #${controllerId}`);
    }
    return decoder;
}

async function onCreate(message: CreateDecoderMessage) {
    const { callbackId, workletPort, controllerId } = message;
    // decoders are pooled with the parent object, so we don't need an object pool here
    debugLog?.log(`-> onCreate(#${controllerId})`);
    const decoder = await OpusDecoder.create(workletPort);
    decoders.set(controllerId, decoder);
    const msg: OperationCompletedDecoderWorkerMessage = {
        type: 'operationCompleted',
        callbackId: callbackId,
    };
    worker.postMessage(msg);
    debugLog?.log(`<- onCreate(#${controllerId})`);
}

function onInit(message: InitDecoderMessage): void {
    const { callbackId, controllerId } = message;
    const decoder = getDecoder(controllerId);
    debugLog?.log(`-> onInit(#${controllerId})`);
    decoder.init();

    const msg: OperationCompletedDecoderWorkerMessage = {
        type: 'operationCompleted',
        callbackId: callbackId,
    };
    worker.postMessage(msg);
    debugLog?.log(`<- onInit(#${controllerId})`);
}

function onData(message: DataDecoderMessage): void {
    const { controllerId, buffer, offset, length } = message;
    const decoder = getDecoder(controllerId);
    const data = buffer.slice(offset, offset + length);
    if (debugOnData)
        debugLog?.log(`onData(#${controllerId}): pushing ${data.byteLength} byte(s)`);
    decoder.pushData(data);
}

function onEnd(message: EndDecoderMessage): void {
    const { controllerId } = message;
    const decoder = getDecoder(controllerId);
    debugLog?.log(`onEnd(#${controllerId})`);
    decoder.pushEnd();
}

function onStop(message: StopDecoderMessage): void {
    const { controllerId } = message;
    const decoder = getDecoder(controllerId);
    debugLog?.log(`-> onStop(#${controllerId})`);
    decoder.stop();
    debugLog?.log(`<- onStop(#${controllerId})`);
}
/// #if DEBUG
self['getDecoder'] = getDecoder;
/// #endif
