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
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { OpusEncoderWorklet } from '../worklets/opus-encoder-worklet-contract';
import { VoiceActivityChange } from './audio-vad-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
import { AudioDiagnosticsState } from '../audio-recorder';
import { Log } from 'logging';
import { approximateGain, clamp } from 'math';

const { logScope, debugLog, infoLog, warnLog, errorLog } = Log.get('OpusEncoderWorker');

interface TimestampedAudioFrame {
    frame: ArrayBuffer;
    timestamp: number;
}

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
/** buffer or callbackId: number of `end` message */
const queue = new Denque<ArrayBuffer>();
const encodedAudioFrames = new Denque<TimestampedAudioFrame>();
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
            encoder = new codecModule.Encoder(AR.SAMPLE_RATE, AE.BIT_RATE);
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
        if (buffer.byteLength === 0 || state !== 'encoding')
            return;

        // debugLog?.log(`onEncoderWorkletSamples(${buffer.byteLength}):`, vadState);
        queue.push(buffer);
        while (queue.length > AE.MAX_BUFFERED_FRAMES) {
            const samplesBuffer = queue.shift();
            void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
        }
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
    const timestamp  = output.timestamp;
    while (encodedAudioFrames.length && encodedAudioFrames.peekFront().timestamp <= timestamp) {
        // release encoded buffers
        const { frame } = encodedAudioFrames.shift();
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }

    audioStream?.addFrame(output, true);
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
    await vadWorker?.reset();
    clearQueue();
    chunkTimeOffset = 0;
}

function processQueue(fade: 'in' | 'out' | 'none' = 'none'): void {
    if (!encoder && !systemEncoder)
        return; // No encoder = there is nothing we can do

    if (fade === 'in') {
        // calculate precise moment when speech starts - VAD may provide signals with delay
        // we will try to filter out frames with gain significantly lower than most recent
        const gains = new Float32Array(queue.length).fill(0);
        if (gains.length) {
            for (let i = 0; i < queue.length; i++) {
                const samplesBuffer = queue.peekAt(i);
                const samples = new Float32Array(samplesBuffer);
                gains[i] = approximateGain(samples);
            }
            const speechGain = gains[gains.length - 1];
            let startIndex = queue.length - 2;
            while (startIndex > 0) {
                const gain = gains[startIndex];
                if (gain < speechGain/5)
                    break;
                startIndex--;
            }
            let framesToShift = clamp(startIndex - 1, 0, queue.length - 1);
            while (framesToShift-- > 0)            {
                const samplesBuffer = queue.shift();
                void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
            }
        }
    }
    else if (!isVoiceDetected)
        return;

    // debugLog?.log(`processQueue:`, fade);
    try {
        while (!queue.isEmpty()) {
            const samplesBuffer = queue.shift();
            const samples = new Float32Array(samplesBuffer);
            const timestamp = chunkTimeOffset * 1000; // microseconds instead of ms
            if (systemEncoder) {
                const audioChunk = new AudioData({
                    format: 'f32-planar',
                    sampleRate: AR.SAMPLE_RATE,
                    numberOfChannels: 1,
                    numberOfFrames: AE.FRAME_SAMPLES,
                    timestamp: timestamp,
                    data: samples,
                });
                systemEncoder.encode(audioChunk);
                encodedAudioFrames.push({ frame: samplesBuffer, timestamp: timestamp });
            }
            else {
                // frameView is a typed_memory_view to Decoder internal buffer, so we have to copy it
                const frameView = encoder.encode(samplesBuffer);
                audioStream?.addFrame(frameView);
                void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
            }

            lastFrameProcessedAt = Date.now();
            chunkTimeOffset += 20;
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
}

function clearQueue(): void {
    while(queue.length) {
        const samplesBuffer = queue.shift();
        // release queued buffers
        void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
    }
    while (encodedAudioFrames.length) {
        // release encoded buffers
        const { frame } = encodedAudioFrames.shift();
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }
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
