/// #if MEM_LEAK_DETECTION
import codec, { Encoder, Codec } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif

import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { handleRpc, RpcResultMessage } from 'rpc';

import { EndMessage, EncoderMessage, InitEncoderMessage, CreateEncoderMessage } from './opus-encoder-worker-message';
import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';
import { VoiceActivityChanged } from './audio-vad';
import { KaiserBesselDerivedWindow } from './kaiser–bessel-derived-window';
import { serializedError, serializedValue } from 'serialized';

const LogScope: string = 'OpusEncoderWorker';

/// #if MEM_LEAK_DETECTION
console.info(`${LogScope}: MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
const codecModuleReady = codec(getEmscriptenLoaderOptions()).then(val => {
    codecModule = val;
    self['codec'] = codecModule;
});

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            if (filename.slice(-4) === 'wasm')
                return codecWasm;
            /// #if MEM_LEAK_DETECTION
            else if (filename.slice(-3) === 'map')
                return codecWasmMap;
            /// #endif
            // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

const CHUNKS_WILL_BE_SENT_ON_RESUME = 4;
const FADE_CHUNKS = CHUNKS_WILL_BE_SENT_ON_RESUME / 2;
const CHUNK_SIZE = 960;

/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;
let hubConnection: signalR.HubConnection;
let recordingSubject = new signalR.Subject<Uint8Array>();
let state: 'inactive' | 'readyToInit' | 'encoding' | 'ended' = 'inactive';
let vadState: 'voice' | 'silence' = 'voice';
let workletPort: MessagePort = null;
let vadPort: MessagePort = null;
let encoder: Encoder;
let lastInitMessage: InitEncoderMessage | null = null;
let isEncoding = false;
let kbdWindow: Float32Array | null = null;
let debug: boolean = false;

/** control flow from the main thread */
worker.onmessage = async (ev: MessageEvent<EncoderMessage>) => handleRpc(
    ev.data.rpcResultId,
    (message) => worker.postMessage(message),
    async () => {
        const request = ev.data;
        switch (request.type) {
            case 'create':
                return await onCreate(request as CreateEncoderMessage, ev.ports[0], ev.ports[1]);
            case 'init':
                return await onInit(request as InitEncoderMessage);
            case 'end':
                return onEnd(request as EndMessage);
            default:
                throw new Error(`Unsupported message type: ${request.type as string}`);
        }
    },
    error => console.error(`${LogScope}.worker.onmessage error:`, error),
);

async function onCreate(message: CreateEncoderMessage, workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void> {
    debug = message.debug;
    if (workletPort != null)
        throw new Error('workletPort has already been set.');
    if (vadPort != null)
        throw new Error('vadPort has already been set.');

    const retryPolicy: signalR.IRetryPolicy = {
        nextRetryDelayInMilliseconds: (retryContext: signalR.RetryContext): number => {
            if (retryContext.previousRetryCount < 5)
                return 100;

            const averageDelay = Math.min(5000, retryContext.elapsedMilliseconds / retryContext.previousRetryCount);
            return averageDelay * (1.2 + Math.random());
        },
    };

    const { audioHubUrl, rpcResultId } = message;
    workletPort = workletMessagePort;
    vadPort = vadMessagePort;
    workletPort.onmessage = onWorkletMessage;
    vadPort.onmessage = onVadMessage;

    // Connect to SignalR Hub
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(audioHubUrl)
        .withAutomaticReconnect(retryPolicy)
        .withHubProtocol(new MessagePackHubProtocol())
        .configureLogging(signalR.LogLevel.Information)
        .build();
    await hubConnection.start();

    // Get fade-in window
    kbdWindow = KaiserBesselDerivedWindow(CHUNK_SIZE*FADE_CHUNKS, 2.55);

    // Setting encoder module
    if (codecModule == null)
        await codecModuleReady;
    encoder = new codecModule.Encoder();
    console.log(`${LogScope}.onCreate, encoder:`, encoder);

    // Notify the host ready to accept 'init' message.
    state = 'readyToInit';
}

async function onInit(message: InitEncoderMessage): Promise<void> {
    const { sessionId, chatId, rpcResultId } = message;
    lastInitMessage = message;

    if (debug)
        console.log(`${LogScope}.onInit`);

    // recordingSubject = new signalR.Subject<Uint8Array>();
    // await hubConnection.send('ProcessAudio', sessionId, chatId, Date.now() / 1000, recordingSubject);

    state = 'encoding';
    vadState = 'silence';
}

function onEnd(message: EndMessage): void {
    state = 'ended';
    processQueue();
    recordingSubject.complete();
}

// Worklet sends messages with raw audio
const onWorkletMessage = (ev: MessageEvent<BufferEncoderWorkletMessage>) => {
    try {
        const { type, buffer } = ev.data;
        // TODO: add offset & length to the message type
        let audioBuffer: ArrayBuffer;
        switch (type) {
        case 'buffer':
            audioBuffer = buffer;
            break;
        default:
            break;
        }
        if (audioBuffer.byteLength === 0)
            return;

        if (state === 'encoding') {
            queue.push(buffer);
            if (vadState === 'voice')
                processQueue();
            else if (queue.length > CHUNKS_WILL_BE_SENT_ON_RESUME)
                queue.shift();
        }
    }
    catch (error) {
        console.error(`${LogScope}.onWorkletMessage error:`, error);
    }
};

const onVadMessage = async (ev: MessageEvent<VoiceActivityChanged>) => {
    try {
        const vadEvent = ev.data;
        if (debug) {
            console.log(`${LogScope}.onVadMessage, data:`, vadEvent);
        }

        const newVadState = vadEvent.kind === 'end'
            ? 'silence'
            : 'voice';

        if (vadState === newVadState)
            return;
        if (state !== 'encoding')
            return;

        if (newVadState === 'silence') {
            // set state, then complete the stream
            vadState = newVadState;
            recordingSubject.complete();
        }
        else {
            if (!lastInitMessage)
                throw new Error('Unable to resume streaming lastNewStreamMessage is null');

            // start new stream and then set state
            const { sessionId, chatId } = lastInitMessage;
            recordingSubject = new signalR.Subject<Uint8Array>();
            await hubConnection.send('ProcessAudio', sessionId, chatId, Date.now() / 1000, recordingSubject);
            vadState = newVadState;
            processQueue('in');
        }
    }
    catch (error) {
        console.error(`${LogScope}.onVadMessage error:`, error);
    }
};

function processQueue(fade: 'in' | 'none' = 'none'): void {
    if (isEncoding)
        return;

    try {
        isEncoding = true;
        let fadeWindowIndex: number | null = null;
        if (fade === 'in' && queue.length >= FADE_CHUNKS )
            fadeWindowIndex = 0;

        while (!queue.isEmpty()) {
            const item = queue.shift();
            if (fadeWindowIndex !== null) {
                const samples = new Float32Array(item);
                for (let i = 0; i < samples.length; i++)
                    samples[i] *= kbdWindow[fadeWindowIndex + i];

                fadeWindowIndex += samples.length;
                if (fadeWindowIndex >= FADE_CHUNKS * CHUNK_SIZE)
                    fadeWindowIndex = null;
            }

            const result = encoder.encode(item);
            const workletMessage: BufferEncoderWorkletMessage = { type: 'buffer', buffer: item };
            workletPort.postMessage(workletMessage, [item]);
            recordingSubject.next(result);
        }
    }
    catch (error) {
        console.error(`${LogScope}.processQueue error:`, error);
    }
    finally {
        isEncoding = false;
    }
}
