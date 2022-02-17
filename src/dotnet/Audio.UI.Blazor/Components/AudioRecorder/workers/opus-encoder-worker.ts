import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { ResolveCallbackMessage } from 'resolve-callback-message';

import { DoneMessage, EncoderMessage, InitNewStreamMessage, LoadModuleMessage } from './opus-encoder-worker-message';
import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';
import { VoiceActivityChanged } from './audio-vad';

interface Encoder {
    init(inputSampleRate: number, channelCount: number, bitsPerSecond: number): void;
    encode(channelBuffers: Float32Array[]): void;
    flush(): ArrayBuffer[];
    close(): void;
}

type WorkerState = 'inactive' | 'readyToInit' | 'encoding' | 'paused' | 'closed';

type ModuleOverrides = {
    locateFile?: (path:string, scriptDirectory: string) => string;
    wasmBinary?: ArrayBuffer;
}

interface QueueItem {
    type: 'buffer' | 'ended';
}

interface BufferQueueItem extends QueueItem {
    buffer: ArrayBuffer;
}

interface EndQueueItem extends QueueItem {
    callbackId: number;
}

const SAMPLE_RATE = 48000;
const CHUNKS_WILL_BE_SENT_ON_RESUME = 3;
const queue = new Denque<BufferQueueItem | EndQueueItem>();
const worker = self as unknown as Worker;
let connection: signalR.HubConnection;
let recordingSubject: signalR.Subject<Uint8Array>;
let state: WorkerState = 'inactive';
let workletPort: MessagePort = null;
let vadPort: MessagePort = null;
let encoder: Encoder;
let lastNewStreamMessage: InitNewStreamMessage | null = null;
let isEncoding = false;
let debugMode = false;

worker.onmessage = async (ev: MessageEvent<EncoderMessage>) => {
    try {
        const { type } = ev.data;
        switch (type) {
            case 'load':
                await onLoadEncoder(ev.data as LoadModuleMessage, ev.ports[0], ev.ports[1]);
                break;

            case 'init':
                await onInitNewStream(ev.data as InitNewStreamMessage);
                break;

            case 'done':
                onDone(ev.data as DoneMessage);
                break;

            default:
                // Ignore
                break;
        }
    }
    catch (error) {
        console.error(error);
    }
};

function onDone(message: DoneMessage) {
    state = 'closed';

    queue.push({ type: 'ended', callbackId: message.callbackId });
    processQueue();
}

async function onInitNewStream(message: InitNewStreamMessage): Promise<void> {
    const { channelCount, bitsPerSecond, sessionId, chatId, callbackId } = message;
    lastNewStreamMessage = message;
    debugMode = message.debugMode;

    encoder.init(SAMPLE_RATE, channelCount, bitsPerSecond);
    state = 'encoding';

    recordingSubject = new signalR.Subject<Uint8Array>();
    await connection.send('ProcessAudio',
        sessionId, chatId, Date.now() / 1000, recordingSubject);

    if (debugMode) {
        console.log('init recorder worker!');
    }

    const initCompletedMessage: ResolveCallbackMessage = {
        callbackId
    };
    worker.postMessage(initCompletedMessage);
}

async function onLoadEncoder(message: LoadModuleMessage, workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void> {
    if (workletPort != null) {
        throw new Error(`EncoderWorker: workletPort has already been specified.`);
    }
    if (vadPort != null) {
        throw new Error(`EncoderWorker: vadPort has already been specified.`);
    }

    const { mimeType, wasmPath, audioHubUrl, callbackId } = message;
    workletPort = workletMessagePort;
    vadPort = vadMessagePort;
    workletPort.onmessage = onWorkletMessage;
    vadPort.onmessage = onVadMessage;
    connection = new signalR.HubConnectionBuilder()
        .withUrl(audioHubUrl)
        .withAutomaticReconnect([0, 300, 500, 1000, 3000, 10000])
        .withHubProtocol(new MessagePackHubProtocol())
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // Connect to the hub endpoint
    await connection.start();

    // Setting encoder module
    const mime = mimeType.toLowerCase();
    let encoderModule: (overrides: ModuleOverrides) => Promise<Encoder>;
    if (mime.indexOf('audio/webm') >= 0) {
        encoderModule = WebMOpusEncoder;
    }
    // Override Emscripten configuration
    const moduleOverrides: ModuleOverrides = {};
    if (wasmPath) {
        moduleOverrides.locateFile = (path, scriptDirectory) =>
            path.match(/\.wasm/i) ? wasmPath : scriptDirectory + path;

        const response = await fetch(wasmPath);
        moduleOverrides.wasmBinary = await response.arrayBuffer();
    }
    // Initialize the module
    // Do not use await - it doesnt' work!
    void encoderModule(moduleOverrides)
        .then(Module => {
            encoder = Module;
            // Notify the host ready to accept 'init' message.
            const readyToInit: ResolveCallbackMessage = {
                callbackId
            }
            worker.postMessage(readyToInit);
            state = 'readyToInit';

            if (debugMode) {
                console.log('load module completed for recorder worker');
            }

        }, reason => {
            // eslint-disable-next-line @typescript-eslint/restrict-template-expressions
            console.error(`Error loading Opus encoder: ${reason}`);
        });
}

const onWorkletMessage = (ev: MessageEvent<BufferEncoderWorkletMessage>) => {
    try {
        const { type, buffer } = ev.data;

        let audioBuffer: ArrayBuffer;
        switch (type) {
            case 'buffer':
                audioBuffer = buffer;
                break;
            default:
                break;
        }
        if (audioBuffer.byteLength !== 0) {
            if (state === 'encoding') {
                queue.push(ev.data);
                processQueue();
            } else if (state === 'paused') {
                queue.push(ev.data);
                if (queue.length > CHUNKS_WILL_BE_SENT_ON_RESUME) {
                    const bufferOrEnded = queue.shift();
                    if (bufferOrEnded.type === 'ended') {
                        queue.shift();
                        queue.unshift(bufferOrEnded);
                    }
                }
            }
        }
    }
    catch (error) {
        console.error(error);
    }
};

const onVadMessage = async (ev: MessageEvent<VoiceActivityChanged>) => {
    try {
        const vadEvent = ev.data;
        if (debugMode) {
            console.log(vadEvent);
        }

        if (state === 'encoding') {
            if (vadEvent.kind === 'end') {
                state = 'paused';
                recordingSubject.complete();
            }
        } else if (state == 'paused' && vadEvent.kind === 'start') {
            if (!lastNewStreamMessage) {
                throw new Error('OpusEncoderWorker: unable to resume streaming lastNewStreamMessage is null');
            }

            const { channelCount, bitsPerSecond, sessionId, chatId } = lastNewStreamMessage;
            encoder.init(SAMPLE_RATE, channelCount, bitsPerSecond);
            recordingSubject = new signalR.Subject<Uint8Array>();
            await connection.send('ProcessAudio', sessionId, chatId, Date.now() / 1000, recordingSubject);

            state = 'encoding';
            processQueue();
        }
    }
    catch(error) {
        console.error(error);
    }
};

function processQueue(): void {
    if (queue.isEmpty()) {
        return;
    }

    if (isEncoding || state === 'paused') {
        return;
    }

    try {
        isEncoding = true;

        const bufferOrEnded = queue.shift();
        if (bufferOrEnded.type === 'ended') {
            const ended = bufferOrEnded as EndQueueItem;
            try {
                if (encoder) {
                    encoder.close();
                    const buffers = encoder.flush();
                    sendEncodedData(buffers);
                }

                const message: ResolveCallbackMessage = {
                    callbackId: ended.callbackId,
                };
                worker.postMessage(message);
            }
            finally {
                recordingSubject.complete();
            }
        }
        else {
            const bufferQueueItem = bufferOrEnded as BufferQueueItem;
            const buffer = bufferQueueItem.buffer;
            const monoPcm = new Float32Array(buffer);
            encoder.encode([monoPcm]);

            const workletMessage: BufferEncoderWorkletMessage = { type: 'buffer', buffer: buffer  };
            workletPort.postMessage(workletMessage, [buffer]);

            const buffers = encoder.flush();
            sendEncodedData(buffers);
        }

    } catch (error) {
        isEncoding = false;
        throw error;

    } finally {
        isEncoding = false;
    }

    processQueue();
}

function sendEncodedData(buffers: ArrayBuffer[]): void {
    // TODO: free!!!
    const totalLength = buffers.reduce((t,buffer) => t + buffer.byteLength, 0);
    const data = new Uint8Array(totalLength);
    buffers.reduce((offset, buffer)=>{
        data.set(new Uint8Array(buffer),offset);
        return offset + buffer.byteLength;
    },0);

    recordingSubject.next(data);
}
