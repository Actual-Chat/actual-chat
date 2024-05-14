import webRtcVadModule, { WebRtcVadModule } from '@actual-chat/webrtc-vad';
import WebRtcVadWasm from '@actual-chat/webrtc-vad/webrtc-vad.wasm';

import Denque from 'denque';
import { Disposable } from 'disposable';
import { Log } from 'logging';
import { Versioning } from 'versioning';
import OnnxModel from './vad.onnx';
import { rpcClientServer, RpcNoWait, rpcNoWait, RpcTimeout } from 'rpc';
import { AudioVadWorklet } from '../worklets/audio-vad-worklet-contract';
import { NNVoiceActivityDetector } from './audio-vad';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';
import { delayAsync, retry } from 'promises';
import { WebRtcVoiceActivityDetector } from './audio-vad-webrtc';
import { RecorderStateEventHandler } from "../opus-media-recorder-contracts";
import { ExponentialMovingAverage } from "./streamed-moving-average";
import { AudioDiagnosticsState } from "../audio-recorder";
import { SAMPLE_RATE, SAMPLES_PER_WINDOW_30, SAMPLES_PER_WINDOW_32 } from '../constants';

const { logScope, debugLog, warnLog, errorLog } = Log.get('AudioVadWorker');

const worker = globalThis as unknown as Worker;
const queue = new Denque<ArrayBuffer>();

let vadModule: WebRtcVadModule = null;
let vadWorklet: AudioVadWorklet & Disposable = null;
let encoderWorker: OpusEncoderWorker & Disposable = null;
let nnVoiceDetector: VoiceActivityDetector = null;
let webrtcVoiceDetector: VoiceActivityDetector = null;
let isVadRunning = false;
let isActive = false;
let isNNVadInitialized = false;
let audioPowerSampleCounter = 0;
let audioPowerAverage = new ExponentialMovingAverage(10);
let canUseNNVad = false;
let lastVadEventProcessedAt = 0;

const serverImpl: AudioVadWorker = {
    create: async (artifactVersions: Map<string, string>, canUseNNVad_: boolean, _timeout?: RpcTimeout): Promise<void> => {
        canUseNNVad = canUseNNVad_;
        if (nnVoiceDetector != null) {
            await nnVoiceDetector.init();
            return;
        }
        else if (webrtcVoiceDetector != null) {
            await webrtcVoiceDetector.init();
            return;
        }

        debugLog?.log(`-> onCreate`);
        Versioning.init(artifactVersions);

        queue.clear();

        // Loading WebRtc VAD module
        vadModule = await retry(3, () => webRtcVadModule(getEmscriptenLoaderOptions()));
        webrtcVoiceDetector = new WebRtcVoiceActivityDetector(new vadModule.WebRtcVad(SAMPLE_RATE, 0));

        debugLog?.log(`<- onCreate`);

        // Init NNVad with delay to avoid excessive load during startup
        const isSimdSupported = canUseNNVad && _isSimdSupported();
        if (isSimdSupported && !isNNVadInitialized) {
            // Load NN VAD Module with delay
            delayAsync(2000).then(_ => initNNVad());
        }
    },

    init: async (workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void> => {
        vadWorklet = rpcClientServer<AudioVadWorklet>(`${logScope}.vadWorklet`, workletPort, serverImpl);
        encoderWorker = rpcClientServer<OpusEncoderWorker>(`${logScope}.encoderWorker`, encoderWorkerPort, serverImpl);

        startWorklet();
        isActive = true;
    },

    reset: async (): Promise<void> => {
        // it is safe to skip init while it still not active
        if (!isActive)
            return;

        nnVoiceDetector?.reset();
        webrtcVoiceDetector?.reset();
    },

    conversationSignal: async (_noWait?: RpcNoWait): Promise<void> => {
        if (!isActive)
            return;

        nnVoiceDetector?.conversationSignal();
        webrtcVoiceDetector?.conversationSignal();
    },

    runDiagnostics: async (diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState> => {
        diagnosticsState.isVadActive = isActive;
        const vad = nnVoiceDetector ?? webrtcVoiceDetector;
        diagnosticsState.lastVadEvent = vad.lastActivityEvent;
        diagnosticsState.lastVadFrameProcessedAt = lastVadEventProcessedAt;

        warnLog?.log('runDiagnostics: ', diagnosticsState);
        return diagnosticsState;
    },

    onFrame: async (buffer: ArrayBuffer): Promise<void> => {
        if (!isActive)
            return;

        if (buffer && buffer.byteLength !== 0) {
            queue.push(buffer);
            void processQueue();
        }
    }
};
const server = rpcClientServer<RecorderStateEventHandler>(`${logScope}.server`, worker, serverImpl);

async function processQueue(): Promise<void> {
    if (isVadRunning)
        return;

    if (webrtcVoiceDetector == null && nnVoiceDetector == null)
        return;

    try {
        isVadRunning = true;
        while (true) {
            if (queue.isEmpty()) {
                return;
            }

            const buffer = queue.shift();
            let vadEvent: VoiceActivityChange | number;
            const hasNNVad = nnVoiceDetector != null;

            // might be switched to NN Vad, but still has queue with wrong buffers
            if (hasNNVad && buffer.byteLength == SAMPLES_PER_WINDOW_32 * 4) {
                const monoPcm = new Float32Array(buffer, 0, SAMPLES_PER_WINDOW_32);
                vadEvent = await nnVoiceDetector!.appendChunk(monoPcm);
            }
            else if (buffer.byteLength == SAMPLES_PER_WINDOW_30 * 4) {
                const monoPcm = new Float32Array(buffer, 0, SAMPLES_PER_WINDOW_30);
                vadEvent = await webrtcVoiceDetector.appendChunk(monoPcm);
            }
            lastVadEventProcessedAt = Date.now();

            void vadWorklet.releaseBuffer(buffer, rpcNoWait);
            // debugLog?.log(`processQueue: vadEvent:`, vadEvent, ', hasNNVad:', hasNNVad);
            if (typeof vadEvent === 'number') {
                audioPowerAverage.append(vadEvent);
                if (audioPowerSampleCounter++ > 10) {
                    // debugLog?.log(`processQueue: lastAverage:`, audioPowerAverage.lastAverage);
                    // Let's sample audio power results to call this once per 300 ms
                    void server.onAudioPowerChange(audioPowerAverage.lastAverage, rpcNoWait);
                    audioPowerSampleCounter = 0;
                }
            }
            else {
                if (vadEvent.kind === "start") {
                    // we are trying to initialize NN vad when WebRTC vad has already triggered recording
                    // because it's time and CPU consuming operation
                    const isSimdSupported = canUseNNVad && _isSimdSupported();
                    if (isSimdSupported && !hasNNVad && !isNNVadInitialized) {
                        // Load NN VAD Module asynchronously
                        void initNNVad();
                    }
                }
                void encoderWorker.onVoiceActivityChange(vadEvent, rpcNoWait);
                void server.onVoiceStateChanged(vadEvent.kind === 'start', rpcNoWait);
            }
        }
    }
    catch (error) {
        errorLog?.log(`processQueue: unhandled error:`, error);
    } finally {
        isVadRunning = false;
    }
}


const _isSimdSupported = (): boolean => {
    // Uncomment following line to test WebRTC VAD on devices supporting SIMD WASM
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


// Helpers

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(WebRtcVadWasm);
            if (filename.slice(-4) === 'wasm')
                return codecWasmPath;
            // /// #if DEBUG
            // else if (filename.slice(-3) === 'map')
            //     return WebRtcVadWasmMap;
            // /// #endif
            // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

async function initNNVad(): Promise<void> {
    if (isNNVadInitialized)
        return;

    const currentActivityEvent: VoiceActivityChange = webrtcVoiceDetector.lastActivityEvent ?? NNVoiceActivityDetector.DefaultVoiceActivity;
    const vad = new NNVoiceActivityDetector(OnnxModel as unknown as URL, currentActivityEvent);
    await vad.init();

    nnVoiceDetector = vad;
    isNNVadInitialized = true;
    startWorklet();
}

function startWorklet(): void {
    if (!vadWorklet)
        return; // when NN VAD is already initialized, but there are no vadWorklet yet

    const hasNNVad = nnVoiceDetector != null;
    const vadWindowSizeMs = hasNNVad ? 32 : 30;
    void vadWorklet.start(vadWindowSizeMs, rpcNoWait);
}
