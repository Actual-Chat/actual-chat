import webRtcVadModule, { WebRtcVadModule } from '@actual-chat/webrtc-vad';
import WebRtcVadWasm from '@actual-chat/webrtc-vad/webrtc-vad.wasm';

import { AUDIO_REC as AR } from '_constants';
import Denque from 'denque';
import { delayAsync, PromiseSource, retry } from 'promises';
import { Disposable } from 'disposable';
import { RunningEMA } from 'math';
import { rpcClientServer, RpcNoWait, rpcNoWait, RpcTimeout } from 'rpc';
import { Versioning } from 'versioning';
import { AudioDiagnosticsState } from "../audio-recorder";
import { NO_VOICE_ACTIVITY, VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { AudioVadWorklet } from '../worklets/audio-vad-worklet-contract';
import { NeuralVoiceActivityDetector, WebRtcVoiceActivityDetector } from './audio-vad';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { RecorderStateServer } from "../opus-media-recorder-contracts";
import OnnxModel from './vad.onnx';
import { Log } from 'logging';

const { logScope, debugLog, infoLog, warnLog, errorLog } = Log.get('AudioVadWorker');

const worker = globalThis as unknown as Worker;
const queue = new Denque<ArrayBuffer>();

let vadWorklet: AudioVadWorklet & Disposable = null;
let encoderWorker: OpusEncoderWorker & Disposable = null;
let isActive = false;
let audioPowerEma = new RunningEMA(0, 10);
let lastVadEventProcessedAt = 0;

class VadLoader {
    private static webRtcVadModule: WebRtcVadModule = null;

    public static neuralVadLoadDelaySource = new PromiseSource<void>();
    public useNeuralVad = false;
    public whenWebRtcVadReady: Promise<void> = null;
    public whenNeuralVadReady: Promise<void> = null;
    public webRtcVad: WebRtcVoiceActivityDetector | null = null;
    public neuralVad: NeuralVoiceActivityDetector | null = null;
    public isInitialized = false;

    public static cancelNeuralVadLoadDelay(): void {
        VadLoader.neuralVadLoadDelaySource?.resolve(undefined)
        VadLoader.neuralVadLoadDelaySource = null;
    }

    public get vad(): VoiceActivityDetector {
        return this.neuralVad ?? this.webRtcVad;
    }

    public get windowSizeMs(): 30 | 32 {
        return this.neuralVad !== null ? 32 : 30;
    }

    public load(useNeuralVad: boolean = true): Promise<void> {
        if (this.whenWebRtcVadReady === null) {
            this.useNeuralVad = useNeuralVad;
            this.whenWebRtcVadReady = (async () => {
                VadLoader.webRtcVadModule ??= await retry(3, () => webRtcVadModule(getEmscriptenLoaderOptions()));
                const baseVad = new VadLoader.webRtcVadModule.WebRtcVad(AR.SAMPLE_RATE, 0);
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
                const lastActivityEvent: VoiceActivityChange = this.webRtcVad.lastActivityEvent ?? NO_VOICE_ACTIVITY;
                const nnVad = new NeuralVoiceActivityDetector(OnnxModel as unknown as URL, lastActivityEvent);
                await nnVad.init();
                queue.clear();
                await vadWorklet.start(vads.windowSizeMs);
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
delayAsync(2000).then(_ => VadLoader.cancelNeuralVadLoadDelay());

const serverImpl: AudioVadWorker = {
    create: async (artifactVersions: Map<string, string>, canUseNNVad: boolean, _timeout?: RpcTimeout): Promise<void> => {
        infoLog?.log(`create`, canUseNNVad, _timeout);
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
        const vad = vads.neuralVad ?? vads.webRtcVad;
        diagnosticsState.isVadActive = isActive;
        diagnosticsState.lastVadEvent = vad.lastActivityEvent;
        diagnosticsState.lastVadFrameProcessedAt = lastVadEventProcessedAt;

        warnLog?.log('runDiagnostics: ', diagnosticsState);
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
    const vad = vads.vad;
    const windowSizeSamples = vads.windowSizeMs * AR.SAMPLES_PER_MS;
    const windowSizeBytes = windowSizeSamples * 4;
    try {
        while (!queue.isEmpty()) {
            const buffer = queue.shift();
            let vadEvent: VoiceActivityChange | number;

            if (buffer.byteLength === windowSizeBytes) {
                const monoPcm = new Float32Array(buffer, 0, windowSizeSamples);
                vadEvent = await vad.appendChunk(monoPcm);
            }
            else {
                warnLog?.log(`processQueue: unexpected buffer length:`, buffer.byteLength);
                vadEvent = 0;
            }
            lastVadEventProcessedAt = Date.now();

            void vadWorklet.releaseBuffer(buffer, rpcNoWait);
            // debugLog?.log(`processQueue: vadEvent:`, vadEvent, ', hasNNVad:', hasNNVad);
            if (typeof vadEvent === 'number') {
                audioPowerEma.appendSample(vadEvent);
                // debugLog?.log(`processQueue: lastAverage:`, audioPowerAverage.value);
                void stateServer.onAudioPowerChange(audioPowerEma.value, rpcNoWait);
            }
            else {
                if (vadEvent.kind === "start") {
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
}

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
