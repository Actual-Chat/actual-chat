// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import codec, { Encoder, Codec } from '@actual-chat/codec';
import codecWasm from '@actual-chat/codec/codec.wasm';
// import codecWasmMap from '@actual-chat/codec/codec.wasm.map';
import { AUDIO, AppConstants, initAppConstants } from 'app-constants';
import Denque from 'denque';
import { Disposable } from 'disposable';
import { retry } from 'actuallab-core';
import { approximateGain, average, clamp } from 'math';
import { rpcClientServer, rpcNoWait, RpcNoWait, RpcTimeout } from 'rpc';
import { Versioning } from 'versioning';

import { Api } from 'api';
import { AudioStream, AudioStreamer } from './audio-streamer';
import { ServerClock } from 'clocks';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { OpusEncoderWorklet } from '../worklets/opus-encoder-worklet-contract';
import { VoiceActivityChange } from './audio-vad-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
import { AudioDiagnosticsState } from '../audio-recorder';
import { ResamplerLoader } from './resampler-loader';
import { WorkerConnectivityUI } from './worker-connectivity-ui';
import { getLogs } from 'logging';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';
import { WebCodecsCompat } from 'web-codecs-compat/init';

const { logScope, debugLog, infoLog, warnLog, errorLog } = getLogs('OpusEncoderWorker');

interface TimestampedAudioFrame {
    frame: ArrayBuffer;
    timestamp: number;
    sourceCapturedAtMs: number;
}

interface TimestampedAudioSamples {
    buffer: ArrayBuffer;
    sourceCapturedAtMs: number;
}

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
/** buffer or callbackId: number of `end` message */
const queue = new Denque<TimestampedAudioSamples>();
const encodedAudioFrames = new Denque<TimestampedAudioFrame>();
const worker = self as unknown as Worker;
let systemCodecConfig: AudioEncoderConfig = null!; // set in create() after initAppConstants

let state: 'initial' | 'created' | 'encoding' | 'ended' = 'initial';
let isVoiceDetected = false;
let isProcessing = false;
let encoderWorklet: OpusEncoderWorklet & Disposable;
let vadWorker: AudioVadWorker & Disposable;
let encoder: Encoder | null;
let systemEncoder: AudioEncoder | null;
let lastStartArguments: { chatId: string, repliedChatEntryId: string } | null = null;
let chunkTimeOffset = 0;
let lastFrameProcessedAt = 0;
let audioStream: AudioStream | null = null;
let encodedFrameCount = 0;
let lastHeartbeatAt = 0;
let heartbeatCheckIntervalId: ReturnType<typeof setInterval> | undefined;

const resamplerLoader = new ResamplerLoader();
// if (DeviceInfo.isFirefox)
//     void resamplerLoader.load();

const serverImpl: OpusEncoderWorker = {
    ...sharedSettingsWorker,

    create: async (appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, apiUrl: string, _timeout?: RpcTimeout): Promise<void> => {
        if (state !== 'initial')
            return; // Already created

        debugLog?.log(`-> create`);
        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
        systemCodecConfig = {
            codec: 'opus',
            numberOfChannels: 1,
            sampleRate: AUDIO.rec.sampleRate,
            bitrate: AUDIO.encode.bitrate,
        };
        Versioning.init(artifactVersions);
        AudioStreamer.init(apiUrl, minLifespanMs => stateServer.getSessionToken(minLifespanMs));
        AudioStreamer.connectionStateChangedEvents.add(x => stateServer.onConnectionStateChanged(x, rpcNoWait));
        // Notify initial connected state — must fire after listener is attached
        if (AudioStreamer.isConnected)
            void stateServer.onConnectionStateChanged(true, rpcNoWait);

        // No-op unless a polyfill is in play; its wasm init is what this waits on.
        await WebCodecsCompat.whenReady;
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
            encoder = new codecModule.Encoder(AUDIO.rec.sampleRate as 48000 | 16000, AUDIO.encode.bitrate);
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
        startHeartbeatWatchdog();
        if (isVoiceDetected)
            await startRecording();
    },

    stop: async (): Promise<void> => {
        debugLog?.log(`stop`);

        state = 'ended';
        isVoiceDetected = false;
        stopHeartbeatWatchdog();
        await stopRecording();
    },

    heartbeat: async (_noWait?: RpcNoWait): Promise<void> => {
        lastHeartbeatAt = Date.now();
    },

    ensureConnected: (_quickReconnect: boolean, _noWait?: RpcNoWait): Promise<void> => {
        return AudioStreamer.ensureConnected();
    },

    disconnectApi: async (_noWait?: RpcNoWait): Promise<void> => {
        // Debug-only path — invoked by DebugUI.disconnectApi(WorkerKind.Recording).
        // Closes the WS connection; the peer's reconnect loop reopens it.
        infoLog?.log(`disconnectApi (debug): disconnecting peer`);
        try {
            if (Api.hub.defaultPeerUrl !== undefined)
                Api.hub.peers.get(Api.hub.defaultPeerUrl)?.disconnect();
        } catch (e) {
            warnLog?.log(`disconnectApi: Api not initialized`, e);
        }
    },

    setRecorderOffset: async (offsetMs: number, _noWait?: RpcNoWait): Promise<void> => {
        infoLog?.log(`setRecorderOffset (debug): ${offsetMs}ms`);
        AudioStreamer.debugOffsetMs = offsetMs;
    },

    runDiagnostics: async (diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> => {
        diagnosticsState.isConnected = AudioStreamer.isConnected;
        diagnosticsState.lastVadFrameProcessedAt = lastFrameProcessedAt;
        infoLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    },

    onEncoderWorkletSamples: async (buffer: ArrayBuffer, capturedAtMs: number, _noWait?: RpcNoWait): Promise<void> => {
        if (buffer.byteLength === 0 || state !== 'encoding')
            return;

        debugLog?.log(`onEncoderWorkletSamples(${buffer.byteLength}):`, approximateGain(new Float32Array(buffer)));
        const sourceCapturedAtMs = capturedAtMs + ServerClock.offsetMs;
        queue.push({ buffer, sourceCapturedAtMs });
        while (queue.length > AUDIO.encode.maxBufferedFrames) {
            const { buffer: samplesBuffer } = queue.shift()!;
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

        infoLog?.log(`onVoiceActivityChange: ${change.kind}, isVoiceDetected=${nextIsVoiceDetected}, state=${state}`);
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
    // Note the shape: a null stream has to fall through to the recovery below. `!audioStream?.
    // isDisposed` returned early for null too, so once a disconnect nulled it, recovery was
    // unreachable and every later frame was discarded even after the connection came back.
    if (audioStream && !audioStream.isDisposed)
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
        const preSkip = encoder?.preSkip ?? AUDIO.encode.defaultPreSkip;
        audioStream = AudioStreamer.addStream(preSkip, lastStartArguments.chatId, '');
    } else {
        audioStream = null;
    }
}

function onSystemEncoderChunk(output: EncodedAudioChunk, metadata: EncodedAudioChunkMetadata): void {
    const timestamp  = output.timestamp;
    let sourceCapturedAtMs: number | undefined;
    while (encodedAudioFrames.length && encodedAudioFrames.peekFront()!.timestamp <= timestamp) {
        // release encoded buffers
        const { frame, sourceCapturedAtMs: frameSourceCapturedAtMs } = encodedAudioFrames.shift()!;
        sourceCapturedAtMs = frameSourceCapturedAtMs;
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }

    ensureAudioStream();
    audioStream?.addFrame(output, true, sourceCapturedAtMs);
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
    const preSkip = encoder?.preSkip ?? AUDIO.encode.defaultPreSkip;
    audioStream = AudioStreamer.addStream(preSkip, chatId, repliedChatEntryId);
    // TODO(AK): fix eslint error
    // eslint-disable-next-line @typescript-eslint/no-floating-promises
    processQueue('in');
}

async function stopRecording(): Promise<void> {
    infoLog?.log(`stopRecording: encodedFrames=${encodedFrameCount}, hasStream=${audioStream !== null}, state=${state}`);
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
                    const { buffer: samplesBuffer } = queue.peekAt(i)!;
                    const samples = new Float32Array(samplesBuffer);
                    gains[i] = approximateGain(samples);
                }
                const preRollFrameLimit = AUDIO.encode.voicePreRollFrameLimit;
                const preRollFrameCount = Math.min(preRollFrameLimit, queue.length);
                const speechFrameCount = Math.max(
                    1,
                    Math.min(preRollFrameCount, Math.ceil(preRollFrameLimit / 3)));
                const speechGain = average(gains.slice(-speechFrameCount));
                const minStartIndex = queue.length - preRollFrameCount;
                let startIndex = queue.length - speechFrameCount - 1;
                while (startIndex >= minStartIndex) {
                    const gain = gains[startIndex];
                    if (gain < speechGain/2)
                        break;

                    startIndex--;
                }
                let framesToShift = startIndex >= minStartIndex
                    ? startIndex - 1
                    : minStartIndex;
                framesToShift = clamp(framesToShift, minStartIndex, queue.length - speechFrameCount);
                debugLog?.log(`processQueue(in): gains: `, gains, framesToShift, speechFrameCount);
                while (framesToShift-- > 0) {
                    const { buffer: samplesBuffer } = queue.shift()!;
                    void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
                }
            }
        }
        else if (!isVoiceDetected)
            return;

        const expectedWindowSizeSamples = 20 * AUDIO.rec.samplesPerMs;
        const expectedWindowSizeBytes = expectedWindowSizeSamples * 4;

        // debugLog?.log(`processQueue:`, fade);
        while (!queue.isEmpty()) {
            const { buffer: samplesBuffer, sourceCapturedAtMs } = queue.shift()!;
            let samples: Float32Array;
            if (samplesBuffer.byteLength === expectedWindowSizeBytes) {
                samples = new Float32Array(samplesBuffer, 0, expectedWindowSizeSamples);
            }
            else {
                // Needs resampling
                const expectedSampleRate = AUDIO.rec.sampleRate;
                const actualSampleRate = Math.floor(samplesBuffer.byteLength / 4 / 20 * 1000 / 100) * 100;
                const resampler = await resamplerLoader.getResampler(actualSampleRate, expectedSampleRate);
                // The output has to be its own array: a view over samplesBuffer is sized for the
                // 48kHz window while the buffer holds the device's smaller one (882 samples at
                // 44.1kHz), so constructing it threw RangeError before resample was even called -
                // every frame dropped, silent sender, on any 44.1kHz device under Firefox.
                samples = resampler.resample(samplesBuffer, new Float32Array(expectedWindowSizeSamples));
                if (samples.length === 0) {
                    // resampler needs more data
                    continue;
                }
            }

            const timestamp = chunkTimeOffset * 1000; // microseconds instead of ms
            if (systemEncoder) {
                const audioChunk = new AudioData({
                    format: 'f32-planar',
                    sampleRate: AUDIO.rec.sampleRate,
                    numberOfChannels: 1,
                    numberOfFrames: AUDIO.encode.frameSamples,
                    timestamp: timestamp,
                    // @ts-expect-error TODO: fix error
                    data: samples,
                });
                systemEncoder.encode(audioChunk);
                encodedAudioFrames.push({ frame: samplesBuffer, timestamp: timestamp, sourceCapturedAtMs });
            }
            else if (encoder) {
                // frameView is a typed_memory_view to Decoder internal buffer, so we have to copy it
                // Emscripten interop requires Uint8Array or ArrayBuffer, so we need to pass Float32Array as Uint8Array
                // @ts-expect-error TODO: fix error
                const frameView = encoder.encode(new Uint8Array(samples.buffer, 0, samples.length * 4));
                ensureAudioStream();
                audioStream?.addFrame(frameView, false, sourceCapturedAtMs);
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
        const { buffer: samplesBuffer } = queue.shift()!;
        // release queued buffers
        void encoderWorklet.releaseBuffer(samplesBuffer, rpcNoWait);
    }
    while (encodedAudioFrames.length) {
        // release encoded buffers
        const { frame } = encodedAudioFrames.shift()!;
        void encoderWorklet.releaseBuffer(frame, rpcNoWait);
    }
}

function startHeartbeatWatchdog(): void {
    // Watchdog runs on the worker's own event loop, so a hung main thread can't suppress it.
    lastHeartbeatAt = Date.now();
    if (heartbeatCheckIntervalId !== undefined)
        return;
    heartbeatCheckIntervalId = setInterval(() => {
        if (state !== 'encoding')
            return;
        if (Date.now() - lastHeartbeatAt <= AUDIO.rec.heartbeat.timeoutMs)
            return;

        warnLog?.log(`heartbeat watchdog: no main-thread heartbeat for ${AUDIO.rec.heartbeat.timeoutMs}ms — shutting down pipeline`);
        state = 'ended';
        isVoiceDetected = false;
        stopHeartbeatWatchdog();
        void stopRecording().catch((e: unknown) => warnLog?.log('heartbeat watchdog: stopRecording failed:', e));
        // Notification sits in main-thread message queue until it wakes — then opusMediaRecorder.stop() cleans up locally.
        void stateServer.onRecorderShutdown('heartbeat-lost', rpcNoWait);
    }, AUDIO.rec.heartbeat.checkIntervalMs);
}

function stopHeartbeatWatchdog(): void {
    if (heartbeatCheckIntervalId === undefined)
        return;
    clearInterval(heartbeatCheckIntervalId);
    heartbeatCheckIntervalId = undefined;
    lastHeartbeatAt = 0;
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
