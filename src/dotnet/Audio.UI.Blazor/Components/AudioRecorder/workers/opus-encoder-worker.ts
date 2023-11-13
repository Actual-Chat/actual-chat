/// #if MEM_LEAK_DETECTION
import codec, {Codec, Encoder} from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import Denque from 'denque';
import {Disposable} from 'disposable';
import {delayAsync, retry} from 'promises';
import {rpcClientServer, rpcNoWait, RpcNoWait, RpcTimeout} from 'rpc';
import * as signalR from '@microsoft/signalr';
import {HubConnectionState} from '@microsoft/signalr';
import {MessagePackHubProtocol} from '@microsoft/signalr-protocol-msgpack';
import {Versioning} from 'versioning';

import {AudioVadWorker} from './audio-vad-worker-contract';
import {KaiserBesselDerivedWindow} from './kaiser–bessel-derived-window';
import {OpusEncoderWorker} from './opus-encoder-worker-contract';
import {OpusEncoderWorklet} from '../worklets/opus-encoder-worklet-contract';
import {VoiceActivityChange} from './audio-vad-contract';
import {RecorderStateEventHandler} from "../opus-media-recorder-contracts";
import {Log} from 'logging';

const { logScope, debugLog, infoLog, warnLog, errorLog } = Log.get('OpusEncoderWorker');

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;

const CHUNKS_WILL_BE_SENT_ON_RESUME = 6; // 20ms * 6 = 120ms
const CHUNKS_WILL_BE_SENT_ON_RECONNECT = 500; // 20ms * 100 = 2s
const FADE_CHUNKS = 3;
const BUFFER_CHUNKS = 4; // 80ms
const CHUNK_SIZE = 960; // 20ms @ 48000KHz

/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;

let hubConnection: signalR.HubConnection;
let recordingSubject: signalR.Subject<Array<Uint8Array>> = null;
let state: 'inactive' | 'created' | 'encoding' | 'ended' = 'inactive';
let vadState: 'voice' | 'silence' = 'silence';
let encoderWorklet: OpusEncoderWorklet & Disposable = null;
let vadWorker: AudioVadWorker & Disposable = null;
let encoder: Encoder | null;
let lastInitArguments: { chatId: string, repliedChatEntryId: string } | null = null;
let lastSessionToken = '';
let isEncoding = false;
let kbdWindow: Float32Array | null = null;
let pinkNoiseChunk: Float32Array | null = null;
let silenceChunk: Float32Array | null = null;
let chunkTimeOffset: number = 0;

const serverImpl: OpusEncoderWorker = {
    create: async (artifactVersions: Map<string, string>, audioHubUrl: string, _timeout?: RpcTimeout): Promise<void> => {
        if (codecModule)
            return;

        debugLog?.log(`-> create`);
        Versioning.init(artifactVersions);

        // Connect to SignalR Hub
        debugLog?.log(`create: hub connecting...`);
        hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(audioHubUrl, {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets,
                // headers: { [SessionTokens.headerName]: lastSessionToken },
            })
            // We use fixed number of attempts here, because the reconnection is anyway
            // triggered after SSB / Stl.Rpc reconnect. See:
            // - C#: ChatAudioUI.ReconnectOnRpcReconnect
            // - TS: AudioRecorder.ctor.
            // Some extra attempts are needed, coz there is a chance that the primary connection
            // stays intact, while this one drops somehow.
            .withAutomaticReconnect([500, 500, 1000, 1000, 1000])
            .withHubProtocol(new MessagePackHubProtocol())
            .configureLogging(signalR.LogLevel.Information)
            .build();
        await hubConnection.start();

        hubConnection.onreconnected(() => onReconnect());
        hubConnection.onreconnecting(() => void server.onConnectionStateChanged(hubConnection.state === HubConnectionState.Connected, rpcNoWait));
        void onReconnect();

        // Ensure audio transport is up and running
        debugLog?.log(`create: -> hub.ping()`);
        const pong = await hubConnection.invoke('Ping');
        debugLog?.log(`create: <- hub.ping():`, pong);
        if (pong !== 'Pong')
            warnLog?.log(`create: unexpected Ping call result`, pong);

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

        state = 'ended';
    },

    start: async (chatId: string, repliedChatEntryId: string): Promise<void> => {
        lastInitArguments = { chatId, repliedChatEntryId };
        debugLog?.log(`start`);

        if (hubConnection.state !== HubConnectionState.Connected)
            await serverImpl.reconnect();
        state = 'encoding';
        if (vadState === 'voice')
            await startRecording();
        // do not set vadState there - it's independent from the recording state
    },

    setSessionToken: async (sessionToken: string, _noWait?: RpcNoWait): Promise<void> => {
        lastSessionToken = sessionToken;
    },

    stop: async (): Promise<void> => {
        debugLog?.log(`stop`);

        state = 'ended';
        vadState = 'silence';
        processQueue('out');
        recordingSubject?.complete();
        recordingSubject = null;
        encoder?.reset();
    },

    reconnect: async (_noWait?: RpcNoWait): Promise<void> => {
        infoLog?.log(`reconnect: `, hubConnection.state);
        if (hubConnection.state === HubConnectionState.Connected)
            return;

        if (hubConnection.state == HubConnectionState.Connecting) {
            // Waiting 1s for connection to happen
            for (let i = 0; i < 10; i++) {
                await delayAsync(100);
                // @ts-ignore
                if (hubConnection.state === HubConnectionState.Connected)
                    return;
            }
        }

        // Reconnect
        if (hubConnection.state === HubConnectionState.Disconnected) {
            await hubConnection.start();
        }
        else {
            await hubConnection.stop();
            await hubConnection.start();
        }
        void onReconnect();
    },

    disconnect: async (_noWait?: RpcNoWait): Promise<void> => {
        infoLog?.log(`disconnect: `, hubConnection.state);
        await hubConnection.stop();
    },

    onEncoderWorkletSamples: async (buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void> => {
        if (buffer.byteLength === 0)
            return;

        const isConnected = hubConnection.state === HubConnectionState.Connected;
        if (state === 'encoding') {
            queue.push(buffer);
            // debugLog?.log(`onEncoderWorkletSamples(${buffer.byteLength}):`, vadState);
            if (vadState === 'voice')
                processQueue();
            else if (isConnected && queue.length > CHUNKS_WILL_BE_SENT_ON_RESUME)
                queue.shift();
            else if (!isConnected && queue.length > CHUNKS_WILL_BE_SENT_ON_RECONNECT)
                queue.shift();
        }
    },

    onVoiceActivityChange: async (change: VoiceActivityChange, _noWait?: RpcNoWait) => {
        const newVadState = change.kind === 'end' ? 'silence' : 'voice';
        if (vadState === newVadState)
            return;

        debugLog?.log(`onVoiceActivityChange:`, change);

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

            await startRecording();
        }
    }
}
const server = rpcClientServer<RecorderStateEventHandler>(`${logScope}.server`, worker, serverImpl);

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
    const { chatId, repliedChatEntryId } = lastInitArguments;

    const isConnected = hubConnection.state === HubConnectionState.Connected;
    if (!isConnected)
        return;

    lastInitArguments.repliedChatEntryId = ""; // We must set it for the first message only

    recordingSubject?.complete(); // Just in case
    recordingSubject = new signalR.Subject<Array<Uint8Array>>();
    if (!encoder)
        encoder = new codecModule.Encoder();
    const preSkip = encoder.preSkip;
    await hubConnection.send('ProcessAudioChunks', lastSessionToken, chatId, repliedChatEntryId, Date.now() / 1000, preSkip, recordingSubject);
    processQueue('in');
}

function processQueue(fade: 'in' | 'out' | 'none' = 'none'): void {
    const isConnected = hubConnection.state === HubConnectionState.Connected;
    // debugLog?.log(`processQueue:(${fade}): isConnected:`, isConnected);
    if (!isConnected)
        return;

    if (isEncoding)
        return;

    if (!encoder)
        return;

    if (queue.length < BUFFER_CHUNKS)
        return;

    try {
        isEncoding = true;
        const result = new Array<Uint8Array>();
        let fadeWindowIndex: number | null = null;
        if (fade === 'in') {
            result.push(encoder.encode(silenceChunk.buffer));
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


            // this fake chunk emulates clicky sound
            // const typedViewFakeChunk = encoder.encode(silenceChunk.buffer);
            // const fakeChunk = new Uint8Array(typedViewFakeChunk.length);
            // fakeChunk.set(typedViewFakeChunk);
            // recordingSubject?.next(fakeChunk);

            // typedViewEncodedChunk is a typed_memory_view to Decoder internal buffer - so you have to copy data
            const typedViewEncodedChunk = encoder.encode(buffer);
            const encodedChunk = new Uint8Array(typedViewEncodedChunk.length);
            encodedChunk.set(typedViewEncodedChunk);
            result.push(encodedChunk);

            void encoderWorklet.releaseBuffer(buffer, rpcNoWait);

            chunkTimeOffset += 20;
        }
        if (fade === 'out') {
            while (chunkTimeOffset < 2200) {
                result.push(encoder.encode(silenceChunk.buffer));
                chunkTimeOffset += 20;
            }
        }

        recordingSubject?.next(result);
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
    finally {
        isEncoding = false;
    }
}

async function onReconnect(): Promise<void> {
    const isConnected = hubConnection.state === HubConnectionState.Connected;
    void server.onConnectionStateChanged(hubConnection.state === HubConnectionState.Connected, rpcNoWait);
    if (isConnected && recordingSubject === null && vadState === 'voice') {
        await startRecording();
    }
}

function getPinkNoiseBuffer(gain: number = 1): Float32Array {
    const buffer = new Float32Array(CHUNK_SIZE);
    let b0: number, b1: number, b2: number, b3: number, b4: number, b5: number, b6: number;
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
