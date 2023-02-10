/// #if MEM_LEAK_DETECTION
import codec, { Codec, Encoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import Denque from 'denque';
import * as signalR from '@microsoft/signalr';
import { HttpTransportType } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { handleRpc } from 'rpc';

import { BufferEncoderWorkletMessage } from '../worklets/opus-encoder-worklet-message';
import { VoiceActivityChanged } from './audio-vad';
import { KaiserBesselDerivedWindow } from './kaiser–bessel-derived-window';
import { CreateEncoderMessage, EndMessage, InitEncoderMessage, StartMessage } from './opus-encoder-worker-message';
import { delayAsync, PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'OpusEncoderWorker';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
const codecModuleReady = loadCodec();

function loadCodec(): Promise<void> {
    // wrapped promise to avoid exceptions with direct call to codec(...)
    return new Promise<void>((resolve,reject) => codec(getEmscriptenLoaderOptions())
        .then(
            val => {
                codecModule = val;
                self['codec'] = codecModule;
                resolve();
            },
            reason => reject(reason)));
}

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
const FADE_CHUNKS = 2;
const CHUNK_SIZE = 960;

/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;

let hubConnection: signalR.HubConnection;
let recordingSubject = new signalR.Subject<Uint8Array>();
let state: 'inactive' | 'created' | 'encoding' | 'ended' = 'inactive';
let vadState: 'voice' | 'silence' = 'voice';
let workletPort: MessagePort = null;
let vadPort: MessagePort = null;
let encoder: Encoder;
let lastInitArguments: { sessionId: string, chatId: string } | null = null;
let isEncoding = false;
let kbdWindow: Float32Array | null = null;
let pinkNoiseChunk: Float32Array | null = null;
let lowNoiseChunk: Float32Array | null = null;
let silenceChunk: Float32Array | null = null;
let chunkTimeOffset: number = 0;

worker.onmessage = async (ev: MessageEvent<CreateEncoderMessage | InitEncoderMessage | StartMessage | EndMessage>) => handleRpc(
    ev.data.rpcResultId,
    (message) => worker.postMessage(message),
    async () => {
        const request = ev.data;
        switch (request.type) {
            case 'create':
                return await onCreate(request as CreateEncoderMessage);
            case 'init':
                return await onInit(request as InitEncoderMessage, ev.ports[0], ev.ports[1]);
            case 'start':
                return await onStart(request as StartMessage);
            case 'end':
                return onEnd();
            default:
                throw new Error(`Unsupported message type: ${request['type'] as string}`);
        }
    },
    error => errorLog?.log(`worker.onmessage: unhandled error:`, error),
);

async function onCreate(message: CreateEncoderMessage): Promise<void> {
    if (workletPort != null)
        throw new Error('workletPort has already been set.');
    if (vadPort != null)
        throw new Error('vadPort has already been set.');
    debugLog?.log(`-> onCreate`);

    const retryPolicy: signalR.IRetryPolicy = {
        nextRetryDelayInMilliseconds: (retryContext: signalR.RetryContext): number => {
            if (retryContext.previousRetryCount < 5)
                return 100;

            const averageDelay = Math.min(5000, retryContext.elapsedMilliseconds / retryContext.previousRetryCount);
            return averageDelay * (1.2 + Math.random());
        },
    };

    // Connect to SignalR Hub
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(message.audioHubUrl, {
            skipNegotiation: true,
            transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect(retryPolicy)
        .withHubProtocol(new MessagePackHubProtocol())
        .configureLogging(signalR.LogLevel.Information)
        .build();
    await hubConnection.start();

    // Get fade-in window
    kbdWindow = KaiserBesselDerivedWindow(CHUNK_SIZE*FADE_CHUNKS, 2.55);
    pinkNoiseChunk = initPinkNoiseBuffer(1.0);
    lowNoiseChunk = initPinkNoiseBuffer(0.005);
    silenceChunk = new Float32Array(CHUNK_SIZE);

    // Setting encoder module
    let retryCount = 0;
    let whenCodecModuleCreated = codecModuleReady;
    while (codecModule == null && retryCount++ < 3) {
        try {
            await whenCodecModuleCreated;
            break;
        }
        catch (e) {
            warnLog.log(e, "error loading codec WASM module.")
            await delayAsync(300);
            whenCodecModuleCreated = loadCodec();
        }
    }
    if (codecModule == null)
        throw new Error("Unable to load codec WASM module.");

    // warmup encoder
    encoder = new codecModule.Encoder();
    for (let i=0; i < 2; i++) {
        encoder.encode(pinkNoiseChunk.buffer);
    }
    encoder.delete();
    encoder = null;

    debugLog?.log(`<- onCreate`);

    state = 'created';
}

async function onInit(message: InitEncoderMessage, workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void> {
    workletPort = workletMessagePort;
    vadPort = vadMessagePort;
    workletPort.onmessage = onWorkletMessage;
    vadPort.onmessage = onVadMessage;

    state = 'ended';
}

async function onStart(message: StartMessage): Promise<void> {
    const { sessionId, chatId } = message;
    lastInitArguments = { sessionId, chatId };

    debugLog?.log(`onStart`);

    encoder = new codecModule.Encoder();

    state = 'encoding';
    vadState = 'silence';
}

function onEnd(): void {
    state = 'ended';
    processQueue('out');
    recordingSubject.complete();
    encoder?.delete();
    encoder = null;
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
        errorLog?.log(`onWorkletMessage: unhandled error:`, error);
    }
};

const onVadMessage = async (ev: MessageEvent<VoiceActivityChanged>) => {
    try {
        const vadEvent = ev.data;
        debugLog?.log(`onVadMessage, data:`, vadEvent);

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
            processQueue('out');
            recordingSubject.complete();
            encoder?.delete();
            encoder = null;
            chunkTimeOffset = 0;
        }
        else {
            if (!lastInitArguments)
                throw new Error('Unable to resume streaming lastNewStreamMessage is null');

            // start new stream and then set state
            const { sessionId, chatId } = lastInitArguments;
            recordingSubject = new signalR.Subject<Uint8Array>();
            if (!encoder)
                encoder = new codecModule.Encoder();
            const preSkip = encoder.preSkip;
            await hubConnection.send('ProcessAudio', sessionId, chatId, Date.now() / 1000, preSkip, recordingSubject);
            vadState = newVadState;
            processQueue('in');
        }
    }
    catch (error) {
        errorLog?.log(`onVadMessage: unhandled error:`, error);
    }
};

function processQueue(fade: 'in' | 'out' | 'none' = 'none'): void {
    if (isEncoding)
        return;

    if (!encoder)
        return;

    try {
        isEncoding = true;
        let fadeWindowIndex: number | null = null;
        if (fade === 'in') {
            const result = encoder.encode(silenceChunk.buffer);
            recordingSubject.next(result);
            chunkTimeOffset = 20;
        }
        else if (fade === 'out') {
            if (queue.length >= FADE_CHUNKS)
                fadeWindowIndex = 0;
        }

        while (!queue.isEmpty()) {
            const item = queue.shift();
            if (fadeWindowIndex !== null) {
                const samples = new Float32Array(item);
                if (fade === 'in')
                    for (let i = 0; i < samples.length; i++)
                        samples[i] *= kbdWindow[fadeWindowIndex + i];
                else if (fade === 'out')
                    for (let i = 0; i < samples.length; i++)
                        samples[i] *= kbdWindow[kbdWindow.length - 1 - fadeWindowIndex - i];

                fadeWindowIndex += samples.length;
                if (fadeWindowIndex >= FADE_CHUNKS * CHUNK_SIZE)
                    fadeWindowIndex = null;
            }

            const result = encoder.encode(item);
            const workletMessage: BufferEncoderWorkletMessage = { type: 'buffer', buffer: item };
            workletPort.postMessage(workletMessage, [item]);
            recordingSubject.next(result);
            chunkTimeOffset += 20;
        }
        if (fade === 'out') {
            while (chunkTimeOffset < 2200) {
                const result = encoder.encode(lowNoiseChunk.buffer);
                recordingSubject.next(result);
                chunkTimeOffset += 20;
            }
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
    finally {
        isEncoding = false;
    }
}

function initPinkNoiseBuffer(gain: number = 1): Float32Array {
    const buffer = new Float32Array(CHUNK_SIZE);
    let b0, b1, b2, b3, b4, b5, b6;
    b0 = b1 = b2 = b3 = b4 = b5 = b6 = 0.0;
    for (let i = 0; i < buffer.length; i++) {
        const white = Math.random() * 2 - 1;
        b0 = 0.99886 * b0 + white * 0.0555179;
        b1 = 0.99332 * b1 + white * 0.0750759;
        b2 = 0.96900 * b2 + white * 0.1538520;
        b3 = 0.86650 * b3 + white * 0.3104856;
        b4 = 0.55000 * b4 + white * 0.5329522;
        b5 = -0.7616 * b5 - white * 0.0168980;
        buffer[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
        buffer[i] *= 0.11;
        buffer[i] *= gain;
        b6 = white * 0.115926;
    }
    return buffer;
}
