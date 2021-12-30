import WebMOpusEncoder from 'opus-media-recorder/WebMOpusEncoder';
import Denque from 'denque';
import {
    EncoderCommand,
    EncoderMessage,
    InitCommand,
    LoadEncoderCommand,
    EncoderWorkletMessage
} from "../opus-media-recorder-message";

interface Encoder {
    init(inputSampleRate: number, channelCount: number, bitsPerSecond: number): void;
    encode(channelBuffers: Float32Array[]): void;
    flush(): ArrayBuffer[];
    close(): void;
}

type WorkerState = 'inactive'|'readyToInit'|'encoding'|'closed';

const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;
let state: WorkerState = 'inactive';
let workletPort: MessagePort = null;
let encoder: Encoder;
let isEncoding: boolean = false;

worker.onmessage = (ev: MessageEvent) => {
    const {command}: EncoderCommand = ev.data;
    switch (command) {
        case 'loadEncoder':
            const {mimeType, wasmPath}: LoadEncoderCommand = ev.data;
            workletPort = ev.ports[0];
            workletPort.onmessage = onWorkletMessage;

            // Setting encoder module
            const mime = mimeType.toLowerCase();
            let encoderModule;
            if (mime.indexOf("audio/webm") >= 0) {
                encoderModule = WebMOpusEncoder;
            }
            // Override Emscripten configuration
            let moduleOverrides = {};
            if (wasmPath) {
                moduleOverrides['locateFile'] = function (path, scriptDirectory) {
                    return path.match(/.wasm/) ? wasmPath : (scriptDirectory + path);
                };
            }
            // Initialize the module
            encoderModule(moduleOverrides).then(Module => {
                encoder = Module;
                // Notify the host ready to accept 'init' message.
                worker.postMessage({command: 'readyToInit'});
                state = 'readyToInit';
            });
            break;

        case 'init':
            const {sampleRate, channelCount, bitsPerSecond}: InitCommand = ev.data;
            encoder.init(sampleRate, channelCount, bitsPerSecond);
            state = 'encoding';
            const message: EncoderMessage = {
                command: 'initCompleted',
            };
            worker.postMessage(message);
            break;

        case 'getEncodedData':
        case 'done':
            if (command === 'done') {
                if (encoder) {
                    encoder.close();
                }
                state = 'closed';
            }

            if (encoder) {
                const buffers = encoder.flush();
                const message: EncoderMessage = {
                    command: command === 'done' ? 'lastEncodedData' : 'encodedData',
                    buffers
                };
                worker.postMessage(message, buffers);
            }

            break;

        default:
            // Ignore
            break;
    }
};

const onWorkletMessage = async (ev: MessageEvent<EncoderWorkletMessage>) => {
    const { topic, buffer }: EncoderWorkletMessage = ev.data;

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

        const _ = processQueue();
    }
};

async function processQueue(): Promise<void> {
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

        const workletMessage: EncoderWorkletMessage = { topic: "buffer", buffer: buffer };
        workletPort.postMessage(workletMessage, [buffer]);

        const buffers = encoder.flush();
        const message: EncoderMessage = {
            command: 'encodedData',
            buffers
        };
        worker.postMessage(message, buffers);

    } catch (error) {
        isEncoding = false;
        throw error;
    } finally {
        isEncoding = false;
    }

    const _ = processQueue();
}
