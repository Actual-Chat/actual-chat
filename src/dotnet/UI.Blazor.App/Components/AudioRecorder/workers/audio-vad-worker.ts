/* eslint-disable @typescript-eslint/require-await,@typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition */
import webRtcVadModule, { WebRtcVadModule } from '@actual-chat/webrtc-vad';
import WebRtcVadWasm from '@actual-chat/webrtc-vad/webrtc-vad.wasm';

import { AUDIO, AppConstants, initAppConstants } from 'app-constants';
import Denque from 'denque';
import { delayAsync, PromiseSource, retry } from 'actuallab-core';
import { Disposable } from 'disposable';
import { RunningEMA } from 'math';
import { rpcClientServer, RpcNoWait, rpcNoWait, RpcTimeout } from 'rpc';
import { Versioning } from 'versioning';
import { AudioDiagnosticsState } from '../audio-recorder';
import { NO_VOICE_ACTIVITY, VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { AudioVadWorklet } from '../worklets/audio-vad-worklet-contract';
import { NeuralVoiceActivityDetector, WebRtcVoiceActivityDetector } from './audio-vad';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { RecorderStateServer } from '../opus-media-recorder-contracts';
// @ts-expect-error intentional import of non-existent file
import OnnxModel from './vad_batched.ort';
import { getLogs } from 'logging';
import { ResamplerLoader } from './resampler-loader';
import { AudioRingBuffer } from '../audio-ring-buffer';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';

const { logScope, debugLog, infoLog, warnLog, errorLog } = getLogs('AudioVadWorker');

const worker = globalThis as unknown as Worker;
const queue = new Denque<ArrayBuffer>();
let vadRingBuffer: AudioRingBuffer = null!; // set in create() after initAppConstants
let vadBuffer: ArrayBufferLike = null!;     // set in create() after initAppConstants

let vadWorklet: AudioVadWorklet & Disposable;
let encoderWorker: OpusEncoderWorker & Disposable;
let isActive = false;
let isProcessing = false;
let lastVadEventProcessedAt = 0;

class VadLoader {
    private static webRtcVadModule: WebRtcVadModule;

    public static neuralVadLoadDelaySource: PromiseSource<void> | undefined = new PromiseSource<void>();
    public useNeuralVad = false;
    public whenWebRtcVadReady: Promise<void>;
    public whenNeuralVadReady: Promise<void>;
    public webRtcVad: WebRtcVoiceActivityDetector | null = null;
    public neuralVad: NeuralVoiceActivityDetector | null = null;
    public isInitialized = false;

    public static cancelNeuralVadLoadDelay(): void {
        VadLoader.neuralVadLoadDelaySource?.resolve(undefined)
        VadLoader.neuralVadLoadDelaySource = undefined;
    }

    public get vad(): VoiceActivityDetector {
        return this.neuralVad ?? this.webRtcVad!;
    }

    public get windowSizeMs(): 30 | 32 {
        return this.neuralVad !== null ? 32 : 30;
    }

    public load(useNeuralVad = true): Promise<void> {
        if (this.whenWebRtcVadReady == null) {
            this.useNeuralVad = useNeuralVad;
            this.whenWebRtcVadReady = (async () => {
                VadLoader.webRtcVadModule ??= await retry(3, () => webRtcVadModule(getWebRTCVadEmscriptenLoaderOptions()));
                const baseVad = new VadLoader.webRtcVadModule.WebRtcVad(AUDIO.rec.sampleRate, 0);
                const webRtcVad = new WebRtcVoiceActivityDetector(baseVad);
                await webRtcVad.init();
                this.webRtcVad = webRtcVad;
            })();
            this.whenNeuralVadReady ??= (async () => {
                if (!this.useNeuralVad) {
                    this.neuralVad = null;
                    return;
                }

                await VadLoader.neuralVadLoadDelaySource;
                infoLog?.log(`VadSwitcher.init: loading neural VAD...`);
                await this.whenWebRtcVadReady;
                const lastActivityEvent: VoiceActivityChange = this.webRtcVad!.lastActivityEvent ?? NO_VOICE_ACTIVITY;
                const nnVad = new NeuralVoiceActivityDetector(OnnxModel as unknown as URL, lastActivityEvent);
                await nnVad.init();
                queue.clear();
                await vadWorklet?.start(vads.windowSizeMs);
                queue.clear();
                this.neuralVad = nnVad;
            })();
            return this.whenWebRtcVadReady;
        }

        // Non-first init(...)
        return (async (): Promise<void> => {
            await this.whenReady();
            // It's safe to skip .init() on what's not loaded yet
            await this.webRtcVad?.init();
            await this.neuralVad?.init();
        })();
    }

    public async whenReady(): Promise<void> {
        return this.whenWebRtcVadReady;
    }

    public async whenFullyReady(forceLoad = true): Promise<void> {
        if (forceLoad)
            VadLoader.cancelNeuralVadLoadDelay();
        return this.useNeuralVad ? this.whenNeuralVadReady : this.whenWebRtcVadReady;
    }
}
const vads = new VadLoader();
void delayAsync(2000).then(() => VadLoader.cancelNeuralVadLoadDelay());

const resamplerLoader = new ResamplerLoader();
// if (DeviceInfo.isFirefox)
//     void resamplerLoader.load();

const serverImpl: AudioVadWorker = {
    ...sharedSettingsWorker,

    create: async (appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, canUseNNVad: boolean, _timeout?: RpcTimeout): Promise<void> => {
        infoLog?.log(`create`, canUseNNVad, _timeout);
        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
        vadRingBuffer = new AudioRingBuffer(AUDIO.vad.neuralFrameSamples * 10, 1);
        vadBuffer = new Float32Array(AUDIO.vad.neuralFrameSamples * 3).buffer;
        Versioning.init(artifactVersions);
        queue.clear();
        void vads.load(canUseNNVad && isSimdSupported());
    },

    init: async (workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void> => {
        await vads.whenReady();
        if (vads.webRtcVad === null) {
            errorLog?.log(`init: no WebRTC VAD`);
            return;
        }

        vadWorklet = rpcClientServer<AudioVadWorklet>(`${logScope}.vadWorklet`, workletPort, serverImpl);
        encoderWorker = rpcClientServer<OpusEncoderWorker>(`${logScope}.encoderWorker`, encoderWorkerPort, serverImpl);
        await vadWorklet.start(vads.windowSizeMs);
        if (vads.neuralVad === null) {
            // Change vadWorklet window size when neural VAD gets loaded
            // TODO(AK): fix this error
            // eslint-disable-next-line @typescript-eslint/no-floating-promises
            vads.whenNeuralVadReady.then(async () => {
                // Load may fail
                if (vads.neuralVad !== null) {
                    queue.clear();
                    await vadWorklet.start(vads.windowSizeMs);
                    queue.clear();
                }
            })
        }
        isActive = true;
    },

    reset: async (): Promise<void> => {
        if (!isActive)
            return;

        vads.webRtcVad?.reset();
        vads.neuralVad?.reset();
        void stateServer.onVoiceStateChanged(false, rpcNoWait);
        queue.clear();
    },

    conversationSignal: async (_noWait?: RpcNoWait): Promise<void> => {
        if (!isActive)
            return;

        vads.webRtcVad?.conversationSignal();
        vads.neuralVad?.conversationSignal();
    },

    runDiagnostics: async (diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> => {
        const vad = vads.neuralVad ?? vads.webRtcVad!;
        diagnosticsState.isVadActive = isActive;
        diagnosticsState.lastVadEvent = vad.lastActivityEvent;
        diagnosticsState.lastVadFrameProcessedAt = lastVadEventProcessedAt;

        infoLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    },

    onFrame: async (buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void> => {
        if (!isActive)
            return;

        if (buffer && buffer.byteLength !== 0) {
            queue.push(buffer);
            void processQueue();
        }
    }
};
const stateServer = rpcClientServer<RecorderStateServer>(`${logScope}.stateServer`, worker, serverImpl);

async function processQueue(): Promise<void> {
    if (isProcessing)
        return;

    const vad = vads.vad;
    const expectedWindowSizeSamples = vads.windowSizeMs * AUDIO.rec.samplesPerMs;
    const expectedWindowSizeBytes = expectedWindowSizeSamples * 4;
    try {
        isProcessing = true;
        while (!queue.isEmpty()) {
            const samplesBuffer = queue.shift()!;
            // let vadEvent: VoiceActivityChange | number = 0;

            if (samplesBuffer.byteLength === expectedWindowSizeBytes) {
                const samples = new Float32Array(samplesBuffer, 0, expectedWindowSizeSamples);
                vadRingBuffer.push([samples]);
            }
            else {
                // Needs resampling
                const expectedSampleRate = AUDIO.rec.sampleRate;
                const actualSampleRate = Math.floor(samplesBuffer.byteLength / 4 / vads.windowSizeMs * 1000 / 100) * 100;
                const resampler = await resamplerLoader.getResampler(actualSampleRate, expectedSampleRate);
                const samples = resampler.resample(samplesBuffer, new Float32Array(samplesBuffer, 0, expectedWindowSizeSamples));
                vadRingBuffer.push([samples]);
                if (samples.length != 0)
                    vadRingBuffer.push([samples]);
            }
            void vadWorklet.releaseBuffer(samplesBuffer, rpcNoWait);

            // Process VAD samples as 3 x expectedWindowSizeSamples - 90ms | 96ms buffer - important to keep in sync with MauiRecorderEngine.cs
            const vadSamples = new Float32Array(vadBuffer, 0, expectedWindowSizeSamples * 3);
            const hasVadSamples = vadRingBuffer.pull([vadSamples])
            if (!hasVadSamples)
                continue;

            lastVadEventProcessedAt = Date.now();
            const vadEvent = await vad.appendChunk(vadSamples);
            if (typeof vadEvent === 'number') {
                if (vad.lastActivityEvent.kind === 'start') // Send gains only when voice activity is detected
                    void stateServer.onAudioPowerChange(vadEvent, rpcNoWait);
            }
            else {
                if (vadEvent.kind === 'start') {
                    if (vads.useNeuralVad && !vads.neuralVad)
                        VadLoader.cancelNeuralVadLoadDelay();
                }
                void encoderWorker.onVoiceActivityChange(vadEvent, rpcNoWait);
                void stateServer.onVoiceStateChanged(vadEvent.kind === 'start', rpcNoWait);
            }
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    }
    finally {
        isProcessing = false;
    }
}

// Helpers

function getWebRTCVadEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(WebRtcVadWasm);
            if (filename.endsWith('wasm'))
                return codecWasmPath;
            // /// #if DEBUG
            // else if (filename.slice(-3) === 'map')
            //     return WebRtcVadWasmMap;
            // /// #endif
            // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.startsWith('data:'))
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

const isSimdSupported = (): boolean => {
    // Uncomment the next line to test WebRTC VAD fallback
    // return false;
    try {
        // Test for WebAssembly SIMD capability (for both browsers and Node.js)
        // This typed array is a WebAssembly program containing SIMD instructions.

        // The binary data is generated from the following code by wat2wasm:
        //
        // (module
        //   (type $t0 (func))
        //   (func $f0 (type $t0)
        //     (drop
        //       (i32x4.dot_i16x8_s
        //         (i8x16.splat
        //           (i32.const 0))
        //         (v128.const i32x4 0x00000000 0x00000000 0x00000000 0x00000000)))))

        return WebAssembly.validate(new Uint8Array([
            0,   97, 115, 109, 1, 0, 0, 0, 1, 4, 1, 96, 0, 0, 3, 2, 1, 0, 10, 30, 1,   28,  0, 65, 0,
            253, 15, 253, 12,  0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0,  0,  253, 186, 1, 26, 11
        ]));
    } catch (e) {
        return false;
    }
};
