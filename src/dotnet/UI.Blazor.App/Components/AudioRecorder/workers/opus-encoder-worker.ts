// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import codec, { Encoder, Codec } from '@actual-chat/codec';
import codecWasm from '@actual-chat/codec/codec.wasm';
// import codecWasmMap from '@actual-chat/codec/codec.wasm.map';
import { AUDIO_REC as AR, AUDIO_ENCODER as AE } from '_constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { retry } from 'promises';
import { approximateGain, average, clamp } from 'math';
import { rpcClientServer, rpcNoWait, RpcNoWait, RpcTimeout } from 'rpc';
import { Versioning } from 'versioning';

import { AudioStream, AudioStreamer } from './audio-streamer';
import { ServerClock } from 'server-clock';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { OpusEncoderWorklet } from '../worklets/opus-encoder-worklet-contract';
import { VoiceActivityChange } from './audio-vad-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
import { AudioDiagnosticsState } from '../audio-recorder';
import { ResamplerLoader } from './resampler-loader';
import { WorkerConnectivityUI } from './worker-connectivity-ui';
import { getLogs } from 'logging';

const { logScope, debugLog, infoLog, warnLog, errorLog } = getLogs('OpusEncoderWorker');

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
let isVoiceDetected = false;
let isProcessing = false;
let encoderWorklet: OpusEncoderWorklet & Disposable;
let vadWorker: AudioVadWorker & Disposable;
let encoder: Encoder | null;
let systemEncoder: AudioEncoder | null;
let lastStartArguments: { chatId: string, repliedChatEntryId: string } | null = null;
let lastSessionToken = '';
let chunkTimeOffset = 0;
let lastFrameProcessedAt = 0;
let audioStream: AudioStream | null = null;
let encodedFrameCount = 0;

const resamplerLoader = new ResamplerLoader();
// if (DeviceInfo.isFirefox)
//     void resamplerLoader.load();

const serverImpl: OpusEncoderWorker = {
    create: async (artifactVersions: Map<string, string>, rpcWsUrl: string, _timeout?: RpcTimeout): Promise<void> => {
        if (state !== 'initial')
            return; // Already created

        debugLog?.log(`-> create`);
        Versioning.init(artifactVersions);
        AudioStreamer.init(rpcWsUrl);
        AudioStreamer.connectionStateChangedEvents.add(x => stateServer.onConnectionStateChanged(x, rpcNoWait));
        // Notify initial connected state — must fire after listener is attached
        if (AudioStreamer.isConnected)
            void stateServer.onConnectionStateChanged(true, rpcNoWait);

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
        if (chatId != null)
            lastStartArguments = { chatId, repliedChatEntryId };
        else if (!lastStartArguments)
            throw new Error('Unable to start recording: chatId is required.');
        debugLog?.log(`start: chatId=${chatId}`);

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

    ensureConnected: (_quickReconnect: boolean, _noWait?: RpcNoWait): Promise<void> => {
        return AudioStreamer.ensureConnected();
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

        debugLog?.log(`onEncoderWorkletSamples(${buffer.byteLength}):`, approximateGain(new Float32Array(buffer)));
        queue.push(buffer);
        while (queue.length > AE.MAX_BUFFERED_FRAMES) {
            const samplesBuffer = queue.shift()!;
            void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
        }
        // TODO(AK): fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-floating-promises
        processQueue();
    },

    onVoiceActivityChange: async (change: VoiceActivityChange, _noWait?: RpcNoWait) => {
        const nextIsVoiceDetected = change.kind !== 'end';
        if (isVoiceDetected === nextIsVoiceDetected)
            return;

        warnLog?.log(`onVoiceActivityChange: ${change.kind}, isVoiceDetected=${nextIsVoiceDetected}, state=${state}`);
        isVoiceDetected = nextIsVoiceDetected;
        if (state !== 'encoding')
            return;

        if (isVoiceDetected)
            await startRecording();
        else
            await stopRecording();
    },

    onConnectivityUpdate: async (isOnline: boolean, isConnected: boolean, isBlazorServer: boolean, _noWait?: RpcNoWait): Promise<void> => {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
    },

    updateServerClockOffset: async (offsetMs: number, _noWait?: RpcNoWait): Promise<void> => {
        ServerClock.updateOffset(Date.now() + offsetMs);
    },
}
const stateServer = rpcClientServer<RecorderStateServer>(`${logScope}.stateServer`, worker, serverImpl);

// System encoder handlers

function onSystemEncoderError(error: DOMException): void {
    errorLog?.log(`onSystemEncoderError:`, error, state)
}

/** Check if audioStream died (e.g. server disconnect) and recreate if voice still active. */
let lastRecoveryAt = 0;
const RECOVERY_MIN_INTERVAL_MS = 2000;
function ensureAudioStream(): void {
    if (!audioStream?.isDisposed)
        return;

    // Don't retry if peer not connected or too soon after last recovery
    if (!AudioStreamer.isConnected) {
        audioStream = null;
        return;
    }

    const now = Date.now();
    if (now - lastRecoveryAt < RECOVERY_MIN_INTERVAL_MS)
        return; // Throttle — stream will be recreated on next frame

    if (state === 'encoding' && isVoiceDetected && lastStartArguments) {
        lastRecoveryAt = now;
        warnLog?.log(`ensureAudioStream: stream disposed, recreating for recovery`);
        const preSkip = encoder?.preSkip ?? AE.DEFAULT_PRE_SKIP;
        audioStream = AudioStreamer.addStream(preSkip, lastStartArguments.chatId, '');
    } else {
        audioStream = null;
    }
}

function onSystemEncoderChunk(output: EncodedAudioChunk, metadata: EncodedAudioChunkMetadata): void {
    const timestamp  = output.timestamp;
    while (encodedAudioFrames.length && encodedAudioFrames.peekFront()!.timestamp <= timestamp) {
        // release encoded buffers
        const { frame } = encodedAudioFrames.shift()!;
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }

    ensureAudioStream();
    audioStream?.addFrame(output, true);
}

async function startRecording(): Promise<void> {
    if (!lastStartArguments)
        throw new Error('Unable to start recording: start(...) must be called first.');

    infoLog?.log(`startRecording: `, lastStartArguments);
    const { chatId, repliedChatEntryId } = lastStartArguments;
    lastStartArguments.repliedChatEntryId = ''; // We reset it for the next message here
    if (audioStream !== null)
        await stopRecording();

    systemEncoder?.configure(systemCodecConfig);
    const preSkip = encoder?.preSkip ?? AE.DEFAULT_PRE_SKIP;
    audioStream = AudioStreamer.addStream(preSkip, chatId, repliedChatEntryId);
    // TODO(AK): fix eslint error
    // eslint-disable-next-line @typescript-eslint/no-floating-promises
    processQueue('in');
}

async function stopRecording(): Promise<void> {
    warnLog?.log(`stopRecording: encodedFrames=${encodedFrameCount}, hasStream=${audioStream !== null}, state=${state}`);
    // TODO(AK): fix eslint error
    // eslint-disable-next-line @typescript-eslint/no-floating-promises
    processQueue('out');
    if (systemEncoder?.state === 'configured')
        await systemEncoder.flush();

    audioStream?.complete();
    audioStream = null;
    encoder?.reset();
    systemEncoder?.reset();
    await vadWorker?.reset();
    clearQueue();
    chunkTimeOffset = 0;
    encodedFrameCount = 0;
}

async function processQueue(fade: 'in' | 'out' | 'none' = 'none'): Promise<void> {
    if (isProcessing)
        return;

    if (!encoder && !systemEncoder)
        return; // No encoder = there is nothing we can do

    try {
        isProcessing = true;
        if (fade === 'in') {
            // calculate precise moment when speech starts - VAD may provide signals with delay
            // we will try to filter out frames with gain significantly lower than most recent
            const gains = new Float32Array(queue.length).fill(0);
            if (gains.length) {
                for (let i = 0; i < queue.length; i++) {
                    const samplesBuffer = queue.peekAt(i)!;
                    const samples = new Float32Array(samplesBuffer);
                    gains[i] = approximateGain(samples);
                }
                const speechGain = average(gains.slice(-5));
                let startIndex = queue.length - 2;
                while (startIndex > 0) {
                    const gain = gains[startIndex];
                    if (gain < speechGain/20)
                        break;
                    startIndex--;
                }
                let framesToShift = clamp(startIndex - 1, 0, queue.length - 5); // Keep at least 4 frames
                debugLog?.log(`processQueue(in): gains: `, gains, framesToShift);
                while (framesToShift-- > 0)            {
                    const samplesBuffer = queue.shift()!;
                    void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
                }
            }
        }
        else if (!isVoiceDetected)
            return;

        const expectedWindowSizeSamples = 20 * AR.SAMPLES_PER_MS;
        const expectedWindowSizeBytes = expectedWindowSizeSamples * 4;

        // debugLog?.log(`processQueue:`, fade);
        while (!queue.isEmpty()) {
            const samplesBuffer = queue.shift()!;
            let samples: Float32Array;
            if (samplesBuffer.byteLength === expectedWindowSizeBytes) {
                samples = new Float32Array(samplesBuffer, 0, expectedWindowSizeSamples);
            }
            else {
                // Needs resampling
                const expectedSampleRate = AR.SAMPLE_RATE;
                const actualSampleRate = Math.floor(samplesBuffer.byteLength / 4 / 20 * 1000 / 100) * 100;
                const resampler = await resamplerLoader.getResampler(actualSampleRate, expectedSampleRate);
                samples = resampler.resample(samplesBuffer, new Float32Array(samplesBuffer, 0, expectedWindowSizeSamples));
                if (samples.length === 0) {
                    // resampler needs more data
                    continue;
                }
            }

            const timestamp = chunkTimeOffset * 1000; // microseconds instead of ms
            if (systemEncoder) {
                const audioChunk = new AudioData({
                    format: 'f32-planar',
                    sampleRate: AR.SAMPLE_RATE,
                    numberOfChannels: 1,
                    numberOfFrames: AE.FRAME_SAMPLES,
                    timestamp: timestamp,
                    // @ts-expect-error TODO: fix error
                    data: samples,
                });
                systemEncoder.encode(audioChunk);
                encodedAudioFrames.push({ frame: samplesBuffer, timestamp: timestamp });
            }
            else if (encoder) {
                // frameView is a typed_memory_view to Decoder internal buffer, so we have to copy it
                // Emscripten interop requires Uint8Array or ArrayBuffer, so we need to pass Float32Array as Uint8Array
                // @ts-expect-error TODO: fix error
                const frameView = encoder.encode(new Uint8Array(samples.buffer, 0, samples.length * 4));
                ensureAudioStream();
                audioStream?.addFrame(frameView);
                void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
            }

            lastFrameProcessedAt = Date.now();
            encodedFrameCount++;
            chunkTimeOffset += 20;
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
    finally {
        isProcessing = false;
    }
}

function clearQueue(): void {
    while(queue.length) {
        const samplesBuffer = queue.shift()!;
        // release queued buffers
        void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
    }
    while (encodedAudioFrames.length) {
        // release encoded buffers
        const { frame } = encodedAudioFrames.shift()!;
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }
}

// Helpers

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(codecWasm);
            if (filename.endsWith('wasm'))
                return codecWasmPath;
            /// #if MEM_LEAK_DETECTION
            else if (filename.endsWith('map'))
                // return codecWasmMap;
                return codecWasmPath + '.map';
                /// #endif
                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.startsWith('data:'))
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}
