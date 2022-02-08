import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

import { EncoderMessage, InitMessage, LoadEncoderMessage } from './opus-encoder-worker-message';
import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';

interface Encoder {
    init(inputSampleRate: number, channelCount: number, bitsPerSecond: number): void;
    encode(channelBuffers: Float32Array[]): void;
    flush(): ArrayBuffer[];
    close(): void;
}

type WorkerState = 'inactive' | 'readyToInit' | 'encoding' | 'closed';

type ModuleOverrides = {
    locateFile?: (path:string, scriptDirectory: string) => string;
}

const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/api/hub/audio')
    .withAutomaticReconnect([0, 300, 500, 1000, 3000, 10000])
    .withHubProtocol(new MessagePackHubProtocol())
    .configureLogging(signalR.LogLevel.Information)
    .build();
const recordingSubject = new signalR.Subject<any>();

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
    if (!encoder) return;

    encoder.close();

    const buffers = encoder.flush();
    const message: EncoderMessage = {
        type: 'lastEncodedData',
        buffers,
    };
    worker.postMessage(message, buffers);
}


function onGetEncodedData() {
    if (!encoder) return;

    const buffers = encoder.flush();
    sendEncodedData(buffers);
}

async function onOnit(command: InitMessage): Promise<void> {
    const { sampleRate, channelCount, bitsPerSecond, sessionId, chatId } = command;
    encoder.init(sampleRate, channelCount, bitsPerSecond);
    state = 'encoding';

    await connection.send('ProcessAudio',
        { SessionId: sessionId, ChatId: chatId, AudioFormat: {}, ClientStartOffset: Date.now() / 1000 },
        // +                { 1: this.sessionId, 2: this.chatId, 3: Date.now() / 1000 },
        recordingSubject);

    const message: EncoderMessage = {
        type: 'initCompleted',
    };
    worker.postMessage(message);
}

async function onLoadEncoder(command: LoadEncoderMessage, port: MessagePort): Promise<void> {
    const { mimeType, wasmPath } = command;
    workletPort = port;
    workletPort.onmessage = onWorkletMessage;

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
    }
    // Initialize the module
    encoder = await encoderModule(moduleOverrides);

    await connection.start();

    // Notify the host ready to accept 'init' message.
    worker.postMessage({command: 'readyToInit'});
    state = 'readyToInit';
}

function onWorkletMessage(ev: MessageEvent<EncoderWorkletMessage>) {
    const { topic, buffer } = ev.data;

    let audioBuffer: ArrayBuffer;
    switch (topic) {
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

        const buffer = queue.pop();
        const monoPcm = new Float32Array(buffer);
        encoder.encode([monoPcm]);

        const workletMessage: BufferEncoderWorkletMessage = { type: 'buffer', buffer: buffer };
        workletPort.postMessage(workletMessage, [buffer]);

        const buffers = encoder.flush();
        // const message: EncoderMessage = {
        //     command: 'encodedData',
        //     buffers
        // };
        // worker.postMessage(message, buffers);
        sendEncodedData(buffers);

    } catch (error) {
        isEncoding = false;
        throw error;

    } finally {
        isEncoding = false;
    }

    processQueue();
}

function sendEncodedData(buffers: ArrayBuffer[]): void {
    const totalLength = buffers.reduce((t,buffer) => t + buffer.byteLength, 0);
    const data = new Uint8Array(totalLength);
    buffers.reduce((offset, buffer)=>{
        data.set(new Uint8Array(buffer),offset);
        return offset + buffer.byteLength;
    },0);


    // // Detect of stop() called before
    // if (command === 'lastEncodedData') {
    //     this.dispatchEvent(new Event('stop'));
    //
    //     this.workerState = 'readyToInit';
    //     if (this.stopResolve) {
    //         const resolve = this.stopResolve;
    //         this.stopResolve = null;
    //         resolve();
    //     }
    // }
}
