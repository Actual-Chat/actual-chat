import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

import { EncoderMessage, InitMessage, LoadEncoderMessage } from './opus-encoder-worker-message';
import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';
import { EncoderResponseMessage } from '../opus-media-recorder-message';

interface Encoder {
    init(inputSampleRate: number, channelCount: number, bitsPerSecond: number): void;
    encode(channelBuffers: Float32Array[]): void;
    flush(): ArrayBuffer[];
    close(): void;
}

type WorkerState = 'inactive' | 'readyToInit' | 'encoding' | 'closed';

type ModuleOverrides = {
    locateFile?: (path:string, scriptDirectory: string) => string;
    wasmBinary?: ArrayBuffer;
}

const queue = new Denque<ArrayBuffer | 'done'>();
const worker = self as unknown as Worker;
let connection: signalR.HubConnection;
let recordingSubject: signalR.Subject<Uint8Array>;

let state: WorkerState = 'inactive';
let workletPort: MessagePort = null;
let encoder: Encoder;
let isEncoding = false;

worker.onmessage = (ev: MessageEvent<EncoderMessage>) => {
    const { type } = ev.data;
    switch (type) {
        case 'loadEncoder':
            void onLoadEncoder(ev.data as LoadEncoderMessage, ev.ports[0]);
            break;

        case 'init':
            void onOnit(ev.data as InitMessage);
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

async function onOnit(message: InitMessage): Promise<void> {
    const { sampleRate, channelCount, bitsPerSecond, sessionId, chatId } = message;
    encoder.init(sampleRate, channelCount, bitsPerSecond);
    state = 'encoding';

    recordingSubject = new signalR.Subject<Uint8Array>();
    await connection.send('ProcessAudio',
        { SessionId: sessionId, ChatId: chatId, ClientStartOffset: Date.now() / 1000 },
        recordingSubject);

    const initCompletedMessage: EncoderResponseMessage = {
        type: 'initCompleted',
    };
    worker.postMessage(initCompletedMessage);
}

async function onLoadEncoder(message: LoadEncoderMessage, port: MessagePort): Promise<void> {
    const { mimeType, wasmPath, audioHubUrl } = message;
    workletPort = port;
    workletPort.onmessage = onWorkletMessage;
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
        }, reason => {
            // eslint-disable-next-line @typescript-eslint/restrict-template-expressions
            console.error(`Error loading Opus encoder: ${reason}`)
        });
}

function onWorkletMessage(ev: MessageEvent<BufferEncoderWorkletMessage>) {
    const { type, buffer } = ev.data;

    let audioBuffer: ArrayBuffer;
    switch (type) {
        case 'buffer':
            audioBuffer = buffer;
            break;
        default:
            break;
    }
    if (audioBuffer.byteLength !== 0 && state === 'encoding') {
        queue.push(buffer);

        processQueue();
    }
}

function processQueue(): void {
    if (queue.isEmpty()) {
        return;
    }

    if (isEncoding) {
        return;
    }

    try {
        isEncoding = true;

        const bufferOrDone = queue.pop();
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
