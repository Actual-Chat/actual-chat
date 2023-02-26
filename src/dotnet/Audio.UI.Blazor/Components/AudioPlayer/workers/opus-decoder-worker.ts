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
const decoders = new Map<number, OpusDecoder>();

let state: 'inactive' | 'created' = 'inactive';

const serverImpl: OpusDecoderWorker = {
    create: async (artifactVersions: Map<string, string>): Promise<void> => {
        debugLog?.log(`-> create`);
        Versioning.init(artifactVersions);

        // Loading codec
        codecModule = await retryAsync(3, () => codec(getEmscriptenLoaderOptions()));

        // Warming up codec
        const decoder = new codecModule.Decoder();
        decoder.delete();

        debugLog?.log(`<- create`);
        state = 'created';
    },

    start: async (controllerId: number, workletMessagePort: MessagePort): Promise<void> => {
        if (state !== 'created')
            throw new Error('Decoder worker has not been created.');

        debugLog?.log(`-> start(#${controllerId})`);
        const existingDecoder = decoders.get(controllerId);
        if (existingDecoder !== undefined) {
            // close existing decoder as AudioContext has been recreated
            existingDecoder.end();
            decoders.delete(controllerId);
        }

        const decoder = new codecModule.Decoder();
        const opusDecoder = await OpusDecoder.create(decoder, workletMessagePort);
        decoders.set(controllerId, opusDecoder);
        debugLog?.log(`<- start(#${controllerId})`);
    },

    stop: async (controllerId: number): Promise<void> => {
        if (state !== 'created')
            throw new Error('Decoder worker has not been created.');

        debugLog?.log(`-> stop(#${controllerId})`);
        const decoder = getDecoder(controllerId);
        decoder.stop();
        debugLog?.log(`<- stop(#${controllerId})`);
    },

    end: async (controllerId: number): Promise<void> => {
        if (state !== 'created')
            throw new Error('Decoder worker has not been created.');

        const decoder = getDecoder(controllerId);
        decoder.end();

        debugLog?.log(`end(#${controllerId})`);
    },

    disposeDecoder: async (controllerId: number): Promise<void> => {
        if (state !== 'created')
            throw new Error('Decoder worker has not been created.');

        const decoder = getDecoder(controllerId);
        decoder.dispose();
        decoders.delete(controllerId);

        debugLog?.log(`end(#${controllerId})`);
    },

    onEncodedChunk: async (
        controllerId: number,
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        _noWait?: RpcNoWait): Promise<void> => {
        if (state !== 'created')
            throw new Error('Decoder worker has not been created.');

        const decoder = getDecoder(controllerId);
        decoder.decode(buffer, offset, length);
    }
};

const server = rpcServer(`${LogScope}.server`, worker, serverImpl);

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


function getDecoder(controllerId: number): OpusDecoder {
    const decoder = decoders.get(controllerId);
    if (decoder === undefined)
        throw new Error(`Can't find decoder object for controller #${controllerId}`);

    return decoder;
}
