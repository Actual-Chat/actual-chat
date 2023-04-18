/// #if DEBUG
import webRtcVadModule, { WebRtcVadModule } from '@actual-chat/webrtc-vad/webrtc-vad.debug';
import WebRtcVadWasm from '@actual-chat/webrtc-vad/webrtc-vad.debug.wasm';
import WebRtcVadWasmMap from '@actual-chat/webrtc-vad/webrtc-vad.debug.wasm.map';
/// #else
/// #code import webRtcVadModule, { WebRtcVadModule } from '@actual-chat/webrtc-vad';
/// #code import WebRtcVadWasm from '@actual-chat/webrtc-vad/webrtc-vad.wasm';
/// #endif

import Denque from 'denque';
import { Disposable } from 'disposable';
import { Log } from 'logging';
import { Versioning } from 'versioning';
import SoxrResampler, { SoxrDatatype, SoxrQuality } from 'wasm-audio-resampler';
import OnnxModel from './vad.onnx';
import SoxrWasm from 'wasm-audio-resampler/app/soxr_wasm.wasm';
import SoxrModule from 'wasm-audio-resampler/src/soxr_wasm';
import { rpcClientServer, RpcNoWait, rpcNoWait, rpcServer, RpcTimeout } from 'rpc';
import { AudioVadWorklet } from '../worklets/audio-vad-worklet-contract';
import { NNVoiceActivityDetector } from './audio-vad';
import { AudioVadWorker } from './audio-vad-worker-contract';
import { OpusEncoderWorker } from './opus-encoder-worker-contract';
import { VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';
import { retry } from 'promises';
import { WebRtcVoiceActivityDetector } from './audio-vad-webrtc';

const { logScope, debugLog, errorLog } = Log.get('AudioVadWorker');

const CHANNELS = 1;
const IN_RATE = 48000;

const worker = globalThis as unknown as Worker;
const queue = new Denque<ArrayBuffer>();
const inputDatatype = SoxrDatatype.SOXR_FLOAT32;
const outputDatatype = SoxrDatatype.SOXR_FLOAT32;
const resampleBuffer = new Uint8Array(512 * 4 * 2);

let vadModule: WebRtcVadModule = null;
let vadWorklet: AudioVadWorklet & Disposable = null;
let encoderWorker: OpusEncoderWorker & Disposable = null;
let resampler: SoxrResampler = null;
let nnVoiceDetector: VoiceActivityDetector = null;
let webrtcVoiceDetector: VoiceActivityDetector = null;
let isVadRunning = false;
let isActive = false;
let isSimdSupported = false;

const serverImpl: AudioVadWorker = {
    create: async (artifactVersions: Map<string, string>, _timeout?: RpcTimeout): Promise<void> => {
        if (resampler != null && nnVoiceDetector != null) {
            await nnVoiceDetector.init();
            return;
        }
        else if (webrtcVoiceDetector != null) {
            await webrtcVoiceDetector.init();
            return;
        }

        debugLog?.log(`-> onCreate`);
        Versioning.init(artifactVersions);

        isSimdSupported = _isSimdSupported();

        queue.clear();

        if (isSimdSupported) {
            const OUT_RATE = 16000;
            resampler = new SoxrResampler(
                CHANNELS,
                IN_RATE,
                OUT_RATE,
                inputDatatype,
                outputDatatype,
                SoxrQuality.SOXR_MQ,
            );
            const soxrWasmPath = Versioning.mapPath(SoxrWasm);
            await resampler.init(SoxrModule, { 'locateFile': () => soxrWasmPath });
            nnVoiceDetector = new NNVoiceActivityDetector(OnnxModel as unknown as URL);
            await nnVoiceDetector.init();
        }
        else {
            // Loading WebRtc VAD module
            vadModule = await retry(3, () => webRtcVadModule(getEmscriptenLoaderOptions()));
            webrtcVoiceDetector = new WebRtcVoiceActivityDetector(new vadModule.WebRtcVad(48000, 0));
        }
        debugLog?.log(`<- onCreate`);
    },

    init: async (workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void> => {
        vadWorklet = rpcClientServer<AudioVadWorklet>(`${logScope}.vadWorklet`, workletPort, serverImpl);
        encoderWorker = rpcClientServer<OpusEncoderWorker>(`${logScope}.encoderWorker`, encoderWorkerPort, serverImpl);
        const vadWindowSizeMs = isSimdSupported ? 32 : 30;
        void vadWorklet.start(vadWindowSizeMs, rpcNoWait);
        isActive = true;
    },

    reset: async (): Promise<void> => {
        // it is safe to skip init while it still not active
        if (!isActive)
            return;

        // resample silence to clean up internal isActive
        if (resampler) {
            const silence = new Uint8Array(768 * 4);
            resampler.processChunk(silence, resampleBuffer);
        }
        nnVoiceDetector?.reset();
        webrtcVoiceDetector?.reset();
    },

    onFrame: async (buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void> => {
        if (!isActive)
            return;

        if (buffer && buffer.byteLength !== 0) {
            queue.push(buffer);
            await processQueue();
        }
    },
};
const server = rpcServer(`${logScope}.server`, worker, serverImpl);

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
            let vadEvent: VoiceActivityChange | null;

            if (isSimdSupported) {
                const dataToResample = new Uint8Array(buffer);
                const resampled = resampler.processChunk(dataToResample, resampleBuffer);
                // 32 ms at 16000 Hz
                const monoPcm = new Float32Array(resampled.buffer, 0, 512);
                vadEvent = await nnVoiceDetector.appendChunk(monoPcm);
            }
            else {
                // 30 ms at 48000 Hz
                const monoPcm = new Float32Array(buffer, 0, 1440);
                vadEvent = await webrtcVoiceDetector.appendChunk(monoPcm);
            }

            void vadWorklet.releaseBuffer(buffer, rpcNoWait);
            if (vadEvent)
                void encoderWorker.onVoiceActivityChange(vadEvent, rpcNoWait);
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
            /// #if DEBUG
            else if (filename.slice(-3) === 'map')
                return WebRtcVadWasmMap;
            /// #endif
            // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}

