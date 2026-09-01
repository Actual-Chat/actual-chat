// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
import codec, { Decoder, Codec } from '@actual-chat/codec';
import codecWasm from '@actual-chat/codec/codec.wasm';
// import codecWasmMap from '@actual-chat/codec/codec.wasm.map';

import { AUDIO, AppConstants, initAppConstants } from 'app-constants';
import { OpusDecoder } from './opus-decoder';
import { OpusDecoderWorker } from './opus-decoder-worker-contract';
import { RpcNoWait, rpcServer, RpcTimeout } from 'rpc';
import { retry } from 'actuallab-core';
import { Versioning } from 'versioning';
import { getLogs } from 'logging';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';
import { WebCodecsCompat } from 'web-codecs-compat/init';

const { logScope, debugLog, errorLog } = getLogs('OpusDecoderWorker');


// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;
let useSystemDecoder = false;

const worker = self as unknown as Worker;
const decoders = new Map<string, OpusDecoder>();
let systemCodecConfig: AudioEncoderConfig = null!; // set in create() after initAppConstants

const serverImpl: OpusDecoderWorker = {
    ...sharedSettingsWorker,

    create: async (appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, _timeout?: RpcTimeout): Promise<void> => {
        debugLog?.log(`-> init`);
        if (codecModule)
            return;

        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
        systemCodecConfig = {
            codec: 'opus',
            numberOfChannels: 1,
            sampleRate: AUDIO.play.sampleRate,
        };
        Versioning.init(artifactVersions);

        // No-op unless a polyfill is in play; its wasm init is what this waits on.
        await WebCodecsCompat.whenReady;
        if (!useSystemDecoder && globalThis.AudioDecoder) {
            const configSupport = await AudioDecoder.isConfigSupported(systemCodecConfig);
            useSystemDecoder = configSupport.supported ?? false;
        }

        if (!useSystemDecoder) {
            // Load & warm-up codec
            codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));
            const decoder = new codecModule.Decoder(AUDIO.play.sampleRate as 48000 | 16000);
            decoder.delete();
        }

        debugLog?.log(`<- init`);
    },

    init: async (streamId: string, feederWorkletPort: MessagePort): Promise<void> => {
        debugLog?.log(`-> #${streamId}.create`);
        const decoder: Decoder | null = useSystemDecoder
            ? null
            : new codecModule!.Decoder(AUDIO.play.sampleRate as 48000 | 16000);
        const opusDecoder = await OpusDecoder.create(streamId, decoder, feederWorkletPort);
        opusDecoder.init();
        decoders.set(streamId, opusDecoder);
        debugLog?.log(`<- #${streamId}.create`);
    },

    resume: async  (streamId: string, sourceRecordedAtMs: number, _noWait?: RpcNoWait): Promise<void> => {
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder) {
            errorLog?.log(`#${streamId}.resume() has failed - decoder does not exist`)
            return;
        }

        opusDecoder.init(sourceRecordedAtMs);
    },

    setTargetBufferSize: async (streamId: string, targetBufferSizeMs: number, _noWait?: RpcNoWait): Promise<void> => {
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder)
            return;

        opusDecoder.setTargetBufferSize(targetBufferSizeMs);
    },

    close: async (streamId: string, _noWait?: RpcNoWait): Promise<void> => {
        debugLog?.log(`#${streamId}.close`);
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder)
            return;

        try {
            await opusDecoder.end(true);
            await opusDecoder.disposeAsync();
        }
        catch (e) {
            errorLog?.log(`#${streamId}.close: error while closing the decoder:`, e);
        }
        finally {
            decoders.delete(streamId);
        }
    },

    end: async (streamId: string, mustAbort: boolean): Promise<void> => {
        debugLog?.log(`#${streamId}.end, mustAbort:`, mustAbort);
        await getDecoder(streamId).end(mustAbort);
    },

    frame: async (
        streamId: string,
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        sourceOffsetMs: number,
        _noWait?: RpcNoWait,
    ): Promise<void> => {
        // debugLog?.log(`#${streamId}.onFrame`);
        getDecoder(streamId).decode(buffer, offset, length, sourceOffsetMs);
    },

    releaseBuffer: async(streamId: string, buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void>  => {
        await getDecoder(streamId).releaseBuffer(buffer, _noWait);
    }
};

const server = rpcServer(`${logScope}.server`, worker, serverImpl);

// Helpers

function getDecoder(streamId: string, failIfNone = true): OpusDecoder {
    const decoder = decoders.get(streamId);
    if (!decoder && failIfNone)
        throw new Error(`getDecoder: no decoder #${streamId}, did you forget to call 'create'?`);

    return decoder!;
}

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
