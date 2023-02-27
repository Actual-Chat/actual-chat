// Commented out because it causes ts compilation issues in webpack release mode
// /// <reference lib="WebWorker" />
// export type { };
// declare const self: WorkerGlobalScope;

/// #if MEM_LEAK_DETECTION
import codec, { Codec, Decoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Encoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif

import { OpusDecoder } from './opus-decoder';
import { Log, LogLevel, LogScope } from 'logging';
import 'logging-init';
import { Versioning } from 'versioning';
import { OpusDecoderWorker } from './opus-decoder-worker-contract';
import { RpcNoWait, rpcServer } from 'rpc';
import { retryAsync } from 'promises';

const LogScope: LogScope = 'OpusDecoderWorker'
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;

const worker = self as unknown as Worker;
const decoders = new Map<string, OpusDecoder>();

const serverImpl: OpusDecoderWorker = {
    init: async (artifactVersions: Map<string, string>): Promise<void> => {
        debugLog?.log(`-> init`);
        Versioning.init(artifactVersions);

        // Load & warm-up codec
        codecModule = await retryAsync(3, () => codec(getEmscriptenLoaderOptions()));
        const decoder = new codecModule.Decoder();
        decoder.delete();

        debugLog?.log(`<- init`);
    },

    create: async (streamId: string, workletMessagePort: MessagePort): Promise<void> => {
        debugLog?.log(`-> #${streamId}.create`);
        let decoder = decoders.get(streamId);
        if (decoder !== undefined) {
            // Recurring 'create' means AudioContext has been replaced, so we should recreate the decoder
            await serverImpl.close(streamId);
        }

        const codecDecoder = new codecModule.Decoder();
        decoder = await OpusDecoder.create(streamId, codecDecoder, workletMessagePort);
        decoders.set(streamId, decoder);
        debugLog?.log(`<- #${streamId}.create`);
    },

    close: async (streamId: string, _noWait?: RpcNoWait): Promise<void> => {
        debugLog?.log(`#${streamId}.dispose`);
        const decoder = getDecoder(streamId, false);
        if (decoder == null)
            return;

        void decoder.disposeAsync(); // No need to wait here
        decoders.delete(streamId);
    },

    stop: async (streamId: string): Promise<void> => {
        debugLog?.log(`#${streamId}.stop`);
        getDecoder(streamId).stop();
    },

    end: async (streamId: string): Promise<void> => {
        debugLog?.log(`#${streamId}.end`);
        await getDecoder(streamId).end();
    },

    onFrame: async (
        streamId: string,
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        _noWait?: RpcNoWait,
    ): Promise<void> => {
        // debugLog?.log(`#${streamId}.onFrame`);
        getDecoder(streamId).decode(buffer, offset, length);
    }
};

const server = rpcServer(`${LogScope}.server`, worker, serverImpl);

// Helpers

function getDecoder(streamId: string, failIfNone = true): OpusDecoder {
    const decoder = decoders.get(streamId);
    if (!decoder && failIfNone)
        throw new Error(`getDecoder: no decoder #${streamId}, did you forget to call 'create'?`);

    return decoder;
}

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


