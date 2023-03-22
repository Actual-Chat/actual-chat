/// #if MEM_LEAK_DETECTION
import codec, { Codec, Encoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import Denque from 'denque';
import { Disposable } from 'disposable';
import { retry } from 'promises';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { Versioning } from 'versioning';

import { AudioVadWorker } from './audio-vad-worker-contract';
import { KaiserBesselDerivedWindow } from './kaiser–bessel-derived-window';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { OpusEncoderWorklet } from '../worklets/opus-encoder-worklet-contract';
import { VoiceActivityChange } from './audio-vad';
import { Log } from 'logging';

const { logScope, debugLog, warnLog, errorLog } = Log.get('OpusEncoderWorker');

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;

const CHUNKS_WILL_BE_SENT_ON_RESUME = 5; // 20ms * 5 = 100ms
const FADE_CHUNKS = 2;
const CHUNK_SIZE = 960; // 20ms @ 48000KHz

/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;

let hubConnection: signalR.HubConnection;
let recordingSubject: signalR.Subject<Uint8Array> = null;
let state: 'inactive' | 'created' | 'encoding' | 'ended' = 'inactive';
let vadState: 'voice' | 'silence' = 'silence';
let encoderWorklet: OpusEncoderWorklet & Disposable = null;
let vadWorker: AudioVadWorker & Disposable = null;
let encoder: Encoder | null;
let lastInitArguments: { sessionId: string, chatId: string, repliedChatEntryId: string } | null = null;
let isEncoding = false;
let kbdWindow: Float32Array | null = null;
let pinkNoiseChunk: Float32Array | null = null;
let silenceChunk: Float32Array | null = null;
let chunkTimeOffset: number = 0;

const serverImpl: OpusEncoderWorker = {
    create: async (artifactVersions: Map<string, string>, audioHubUrl: string): Promise<void> => {
        if (encoderWorklet != null || vadWorker != null)
            throw new Error('Already initialized.');

        debugLog?.log(`-> create`);
        Versioning.init(artifactVersions);

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
            .withUrl(audioHubUrl, {
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
        pinkNoiseChunk = getPinkNoiseBuffer(1.0);
        silenceChunk = new Float32Array(CHUNK_SIZE);

        // Loading codec
        codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));

        // Warming up codec
        encoder = new codecModule.Encoder();
        for (let i=0; i < 2; i++)
            encoder.encode(pinkNoiseChunk.buffer);
        encoder.reset();

        debugLog?.log(`<- create`);
        state = 'created';
    },

    init: async (workletPort: MessagePort, vadPort: MessagePort): Promise<void> => {
        encoderWorklet = rpcClientServer<OpusEncoderWorklet>(`${logScope}.encoderWorklet`, workletPort, serverImpl);
        vadWorker = rpcClientServer<AudioVadWorker>(`${logScope}.vadWorker`, vadPort, serverImpl);

        // Ensure audio transport is up and running
        debugLog?.log(`init: -> hub.ping()`);
        const pong = await hubConnection.invoke('Ping');
        debugLog?.log(`init: <- hub.ping(): `, pong);
        if (pong !== 'Pong')
            warnLog?.log(`init: unexpected Ping call result`, pong);

        state = 'ended';
    },

    start: async (sessionId: string, chatId: string, repliedChatEntryId: string): Promise<void> => {
        lastInitArguments = { sessionId, chatId, repliedChatEntryId };
        debugLog?.log(`start`);

        state = 'encoding';
        if (vadState === 'voice')
            await startRecording();
        // do not set vadState there - it's independent from the recording state
    },

    stop: async (): Promise<void> => {
        debugLog?.log(`stop`);

        state = 'ended';
        processQueue('out');
        recordingSubject?.complete();
        recordingSubject = null;
        encoder?.reset();
    },

    onEncoderWorkletSamples: async (buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void> => {
        if (buffer.byteLength === 0)
            return;

        if (state === 'encoding') {
            queue.push(buffer);
            if (vadState === 'voice')
                processQueue();
            else if (queue.length > CHUNKS_WILL_BE_SENT_ON_RESUME)
                queue.shift();
        }
    },

    onVoiceActivityChange: async (change: VoiceActivityChange, _noWait?: RpcNoWait) => {
        debugLog?.log(`onVoiceActivityChange:`, change);

        const newVadState = change.kind === 'end' ? 'silence' : 'voice';
        if (vadState === newVadState)
            return;
        if (state !== 'encoding') {
            // set state, then leave since we are not recording
            vadState = newVadState;
            return;
        }

        if (newVadState === 'silence') {
            // set state, then complete the stream
            vadState = newVadState;
            processQueue('out');
            recordingSubject?.complete();
            recordingSubject = null;
            encoder?.delete();
            encoder = null;
            chunkTimeOffset = 0;
        }
        else {
            // set state, then start new stream - several audio chunks can be buffered at the recordingSubject
            // while hubConnection.send is being processed
            vadState = newVadState;

            if (!lastInitArguments)
                throw new Error('Unable to resume streaming lastNewStreamMessage is null');

            // start new stream and then set state
            lastInitArguments.repliedChatEntryId = ""; // We must set it for the first message only

            await startRecording();
        }
    }
}
const server = rpcServer(`${logScope}.server`, worker, serverImpl);

// Helpers

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(codecWasm);
            if (filename.slice(-4) === 'wasm')
                return codecWasmPath;
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

async function startRecording(): Promise<void> {
    const { sessionId, chatId, repliedChatEntryId } = lastInitArguments;

    recordingSubject?.complete(); // Just in case
    recordingSubject = new signalR.Subject<Uint8Array>();
    if (!encoder)
        encoder = new codecModule.Encoder();
    const preSkip = encoder.preSkip;
    await hubConnection.send('ProcessAudio', sessionId, chatId, repliedChatEntryId, Date.now() / 1000, preSkip, recordingSubject);
    processQueue('in');
}

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
            recordingSubject?.next(result);
            chunkTimeOffset = 20;
        }
        else if (fade === 'out') {
            if (queue.length >= FADE_CHUNKS)
                fadeWindowIndex = 0;
        }

        while (!queue.isEmpty()) {
            const buffer = queue.shift();
            if (fadeWindowIndex !== null) {
                const samples = new Float32Array(buffer);
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

            // typedViewEncodedChunk is a typed_memory_view to Decoder internal buffer - so you have to copy data
            const typedViewEncodedChunk = encoder.encode(buffer);
            void encoderWorklet.releaseBuffer(buffer, rpcNoWait);
            const encodedChunk = new Uint8Array(typedViewEncodedChunk.length);
            encodedChunk.set(typedViewEncodedChunk);
            recordingSubject?.next(encodedChunk);
            chunkTimeOffset += 20;
        }
        if (fade === 'out') {
            while (chunkTimeOffset < 2200) {
                const result = encoder.encode(silenceChunk.buffer);
                recordingSubject?.next(result);
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

function getPinkNoiseBuffer(gain: number = 1): Float32Array {
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
