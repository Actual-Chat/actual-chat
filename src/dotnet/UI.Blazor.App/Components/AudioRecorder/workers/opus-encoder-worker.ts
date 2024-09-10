/// #if MEM_LEAK_DETECTION
import codec, { Codec, Encoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import { AUDIO_REC as AR, AUDIO_ENCODER as AE } from '_constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { retry } from 'promises';
import { rpcClientServer, rpcNoWait, RpcNoWait, RpcTimeout } from 'rpc';
import { Versioning } from 'versioning';

import { AudioStream, AudioStreamer } from './audio-streamer';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { KaiserBesselDerivedWindow } from './kaiser–bessel-derived-window';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { OpusEncoderWorklet } from '../worklets/opus-encoder-worklet-contract';
import { VoiceActivityChange } from './audio-vad-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
import { AudioDiagnosticsState } from '../audio-recorder';
import { Log } from 'logging';

const { logScope, debugLog, infoLog, warnLog, errorLog } = Log.get('OpusEncoderWorker');

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
// DEBUG - required for a chaos monkey
// const sockets: WebSocket[] = [];

/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const worker = self as unknown as Worker;
const systemCodecConfig: AudioEncoderConfig = {
    codec: 'opus',
    numberOfChannels: 1,
    sampleRate: AR.SAMPLE_RATE,
    bitrate: AE.BIT_RATE,
};

let state: 'initial' | 'created' | 'encoding' | 'ended' = 'initial';
let isVoiceDetected: boolean = false;
let encoderWorklet: OpusEncoderWorklet & Disposable = null;
let vadWorker: AudioVadWorker & Disposable = null;
let encoder: Encoder | null;
let systemEncoder: AudioEncoder | null;
let lastStartArguments: { chatId: string, repliedChatEntryId: string } | null = null;
let lastSessionToken = '';
let kbdWindow: Float32Array | null = null;
let pinkNoiseChunk: Float32Array | null = null;
let chunkTimeOffset: number = 0;
let lastFrameProcessedAt = 0;
let audioStream: AudioStream | null = null;

const serverImpl: OpusEncoderWorker = {
    create: async (artifactVersions: Map<string, string>, hubUrl: string, _timeout?: RpcTimeout): Promise<void> => {
        if (state !== 'initial')
            return; // Already created

        debugLog?.log(`-> create`);
        Versioning.init(artifactVersions);
        AudioStreamer.init(hubUrl);
        AudioStreamer.connectionStateChangedEvents.add(x => stateServer.onConnectionStateChanged(x, rpcNoWait));

        // Get fade-in window
        kbdWindow = KaiserBesselDerivedWindow(AE.FRAME_SAMPLES * AE.FADE_FRAMES, 2.55);
        pinkNoiseChunk = getPinkNoiseBuffer(1.0);

        if (!systemEncoder && globalThis.AudioEncoder) {
            const configSupport = await AudioEncoder.isConfigSupported(systemCodecConfig);
            if (configSupport.supported) {
                infoLog?.log(`create: will use AudioEncoder`);
                systemEncoder = new AudioEncoder({
                    error: onSystemEncoderError,
                    output: onSystemEncoderChunk,
                });
            }
        }

        if (!systemEncoder && !encoder) {
            infoLog?.log(`create: will use WASM codec`);
            // Loading codec
            codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));
            // Warming up codec
            encoder = new codecModule.Encoder(AR.SAMPLE_RATE, AE.BIT_RATE);
            for (let i = 0; i < 2; i++)
                encoder.encode(pinkNoiseChunk.buffer);
            encoder.reset();
            debugLog?.log(`create: WASM codec is ready`);
        }

        state = 'created';
        debugLog?.log(`<- create`);
    },

    init: async (workletPort: MessagePort, vadPort: MessagePort): Promise<void> => {
        encoderWorklet = rpcClientServer<OpusEncoderWorklet>(`${logScope}.encoderWorklet`, workletPort, serverImpl);
        vadWorker = rpcClientServer<AudioVadWorker>(`${logScope}.vadWorker`, vadPort, serverImpl);
        state = 'ended';
    },

    start: async (chatId: string, repliedChatEntryId: string): Promise<void> => {
        lastStartArguments = { chatId, repliedChatEntryId };
        debugLog?.log(`start`);

        state = 'encoding';
        if (isVoiceDetected)
            await startRecording();
    },

    setSessionToken: async (sessionToken: string, _noWait?: RpcNoWait): Promise<void> => {
        lastSessionToken = sessionToken;
    },

    stop: async (): Promise<void> => {
        debugLog?.log(`stop`);

        state = 'ended';
        isVoiceDetected = false;
        await stopRecording();
    },

    ensureConnected: (quickReconnect: boolean, _noWait?: RpcNoWait): Promise<void> => {
        return AudioStreamer.ensureConnected(quickReconnect);
    },

    disconnect: (_noWait?: RpcNoWait): Promise<void> => {
        return AudioStreamer.disconnect();
    },

    runDiagnostics: async (diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> => {
        diagnosticsState.isConnected = AudioStreamer.isConnected;
        diagnosticsState.lastVadFrameProcessedAt = lastFrameProcessedAt;
        warnLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    },

    onEncoderWorkletSamples: async (buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void> => {
        if (buffer.byteLength === 0 || state !== 'encoding' || !isVoiceDetected)
            return;

        // debugLog?.log(`onEncoderWorkletSamples(${buffer.byteLength}):`, vadState);
        queue.push(buffer);
        while (queue.length > AE.MAX_BUFFERED_FRAMES)
            queue.shift();
        processQueue();
    },

    onVoiceActivityChange: async (change: VoiceActivityChange, _noWait?: RpcNoWait) => {
        const nextIsVoiceDetected = change.kind !== 'end';
        if (isVoiceDetected === nextIsVoiceDetected)
            return;

        debugLog?.log(`onVoiceActivityChange:`, change);
        isVoiceDetected = nextIsVoiceDetected;
        if (state !== 'encoding')
            return;

        if (isVoiceDetected)
            await startRecording();
        else
            await stopRecording();
    },
}
const stateServer = rpcClientServer<RecorderStateServer>(`${logScope}.stateServer`, worker, serverImpl);

// System encoder handlers

function onSystemEncoderError(error: DOMException): void {
    errorLog?.log(`onSystemEncoderError:`, error, state)
}

function onSystemEncoderChunk(output: EncodedAudioChunk, metadata: EncodedAudioChunkMetadata): void {
    audioStream?.addFrame(output, true);
}

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
    if (!lastStartArguments)
        throw new Error('Unable to start recording: start(...) must be called first.');

    infoLog?.log(`startRecording: `, lastStartArguments);
    const { chatId, repliedChatEntryId } = lastStartArguments;
    lastStartArguments.repliedChatEntryId = ""; // We reset it for the next message here
    if (audioStream !== null)
        await stopRecording();

    systemEncoder?.configure(systemCodecConfig);
    const preSkip = encoder?.preSkip ?? AE.DEFAULT_PRE_SKIP;
    audioStream = AudioStreamer.addStream(lastSessionToken, preSkip, chatId, repliedChatEntryId);
    processQueue('in');
}

async function stopRecording(): Promise<void> {
    processQueue('out');
    if (systemEncoder && systemEncoder.state === 'configured')
        await systemEncoder.flush();

    audioStream?.complete();
    audioStream = null;
    encoder?.reset();
    systemEncoder?.reset();
    queue.clear();
    chunkTimeOffset = 0;
}

function processQueue(fade: 'in' | 'out' | 'none' = 'none'): void {
    if (!encoder && !systemEncoder)
        return; // No encoder = there is nothing we can do

    // debugLog?.log(`processQueue:`, fade);
    try {
        let fadeWindowIndex: number | null = null;
        if (fade === 'in' || fade === 'out')
            fadeWindowIndex = 0;

        while (!queue.isEmpty()) {
            const samplesBuffer = queue.shift();
            const samples = new Float32Array(samplesBuffer);
            if (fadeWindowIndex !== null) {
                if (fade === 'in')
                    for (let i = 0; i < samples.length; i++)
                        samples[i] *= kbdWindow[fadeWindowIndex + i];
                else if (fade === 'out')
                    for (let i = 0; i < samples.length; i++)
                        samples[i] *= kbdWindow[kbdWindow.length - 1 - fadeWindowIndex - i];

                fadeWindowIndex += samples.length;
                if (fadeWindowIndex >= AE.FADE_FRAMES * AE.FRAME_SAMPLES)
                    fadeWindowIndex = null;
            }


            // this fake chunk emulates clicky sound
            // const typedViewFakeChunk = encoder.encode(silenceChunk.buffer);
            // const fakeChunk = new Uint8Array(typedViewFakeChunk.length);
            // fakeChunk.set(typedViewFakeChunk);
            // recordingSubject?.next(fakeChunk);

            if (systemEncoder) {
                const audioChunk = new AudioData({
                    format: 'f32-planar',
                    sampleRate: AR.SAMPLE_RATE,
                    numberOfChannels: 1,
                    numberOfFrames: AE.FRAME_SAMPLES,
                    timestamp: chunkTimeOffset * 1000, // microseconds instead of ms
                    data: samples,
                });
                systemEncoder.encode(audioChunk);
            }
            else {
                // frameView is a typed_memory_view to Decoder internal buffer, so we have to copy it
                const frameView = encoder.encode(samplesBuffer);
                audioStream?.addFrame(frameView);
            }

            lastFrameProcessedAt = Date.now();
            void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);

            chunkTimeOffset += 20;
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
}

function getPinkNoiseBuffer(gain: number = 1): Float32Array {
    const buffer = new Float32Array(AE.FRAME_SAMPLES);
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
