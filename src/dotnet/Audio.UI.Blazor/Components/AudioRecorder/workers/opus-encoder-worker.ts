import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

import { EncoderMessage, InitNewStreamMessage, LoadModuleMessage } from './opus-encoder-worker-message';
import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';
import { EncoderResponseMessage } from '../opus-media-recorder-message';
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

const CHUNKS_WILL_BE_SENT_ON_RESUME = 3;
const queue = new Denque<ArrayBuffer | 'done'>();
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

worker.onmessage = (ev: MessageEvent<EncoderMessage>) => {
    const { type } = ev.data;
    switch (type) {
        case 'load-module':
            void onLoadEncoder(ev.data as LoadModuleMessage, ev.ports[0], ev.ports[1]);
            break;

        case 'init-new-stream':
            void onInitNewStream(ev.data as InitNewStreamMessage);
            break;

        case 'done':
            onDone();
            break;

        default:
            // Ignore
            break;
    }
};

function onDone() {
    state = 'closed';

    queue.push('done');
    processQueue();
}

async function onInitNewStream(message: InitNewStreamMessage): Promise<void> {
    const { sampleRate, channelCount, bitsPerSecond, sessionId, chatId } = message;
    lastNewStreamMessage = message;
    debugMode = message.debugMode;

    encoder.init(sampleRate, channelCount, bitsPerSecond);
    state = 'encoding';

    recordingSubject = new signalR.Subject<Uint8Array>();
    await connection.send('ProcessAudio',
        sessionId, chatId, Date.now() / 1000, recordingSubject);

    if (debugMode) {
        console.log('init recorder worker!');
    }

    const initCompletedMessage: EncoderResponseMessage = {
        type: 'initCompleted',
    };
    worker.postMessage(initCompletedMessage);
}

async function onLoadEncoder(message: LoadModuleMessage, workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void> {
    const { mimeType, wasmPath, audioHubUrl } = message;
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
            path.match(/.wasm/) ? wasmPath : scriptDirectory + path;

        const response = await fetch(wasmPath);
        moduleOverrides.wasmBinary = await response.arrayBuffer();
    }
    // Initialize the module
    // Do not use await - it doesnt' work!
    void encoderModule(moduleOverrides)
        .then(Module => {
            encoder = Module;
            // Notify the host ready to accept 'init' message.
            const readyToInit: EncoderResponseMessage = {
                type: 'readyToInit'
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
        if (state === 'encoding'){
            queue.push(buffer);
            processQueue();
        } else if (state === 'paused') {
            queue.push(buffer);
            if (queue.length > CHUNKS_WILL_BE_SENT_ON_RESUME) {
                const bufferOrDone = queue.shift();
                if (bufferOrDone === 'done') {
                    queue.shift();
                    queue.unshift('done');
                }
            }
        }
    }
};

const onVadMessage = async (ev: MessageEvent<VoiceActivityChanged>) => {
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

        const { sampleRate, channelCount, bitsPerSecond, sessionId, chatId } = lastNewStreamMessage;
        encoder.init(sampleRate, channelCount, bitsPerSecond);
        recordingSubject = new signalR.Subject<Uint8Array>();
        await connection.send('ProcessAudio', sessionId, chatId, Date.now() / 1000, recordingSubject);

        state = 'encoding';
        processQueue();
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

        const bufferOrDone = queue.shift();
        if (typeof bufferOrDone === 'string') {
            if (bufferOrDone === 'done') {
                try {
                    if (encoder) {
                        encoder.close();
                        const buffers = encoder.flush();
                        sendEncodedData(buffers);
                    }

                    const message: EncoderResponseMessage = {
                        type: 'doneCompleted'
                    };
                    worker.postMessage(message);
                }
                finally {
                    recordingSubject.complete();
                }
            }
        }
        else {
            const buffer = bufferOrDone;
            const monoPcm = new Float32Array(buffer);
            encoder.encode([monoPcm]);

            const workletMessage: BufferEncoderWorkletMessage = { type: 'buffer', buffer: buffer };
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
