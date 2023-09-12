 // Commented out because it causes ts compilation issues in webpack release mode
// /// <reference lib="WebWorker" />
// export type { };
// declare const self: WorkerGlobalScope;

/// #if MEM_LEAK_DETECTION
import codec, { Codec, Decoder } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Decoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif

import { OpusDecoder } from './opus-decoder';
import { ObjectPool } from 'object-pool';
import { OpusDecoderWorker } from './opus-decoder-worker-contract';
import { RpcNoWait, rpcServer, RpcTimeout } from 'rpc';
import { retry } from 'promises';
import { Versioning } from 'versioning';
import { Log } from 'logging';

const { logScope, debugLog, errorLog } = Log.get('OpusDecoderWorker');


// TODO: create wrapper around module for all workers

let codecModule: Codec | null = null;

const worker = self as unknown as Worker;
const decoders = new Map<string, OpusDecoder>();

const serverImpl: OpusDecoderWorker = {
    create: async (artifactVersions: Map<string, string>, _timeout?: RpcTimeout): Promise<void> => {
        debugLog?.log(`-> init`);
        if (codecModule)
            return;

        Versioning.init(artifactVersions);

        // Load & warm-up codec
        codecModule = await retry(3, () => codec(getEmscriptenLoaderOptions()));
        const decoder = new codecModule.Decoder();
        decoder.delete();

        debugLog?.log(`<- init`);
    },

    init: async (streamId: string, feederWorkletPort: MessagePort): Promise<void> => {
        debugLog?.log(`-> #${streamId}.create`);
        await serverImpl.close(streamId);
        const decoder = new codecModule.Decoder();
        const opusDecoder = await OpusDecoder.create(streamId, decoder, feederWorkletPort);
        decoders.set(streamId, opusDecoder);
        debugLog?.log(`<- #${streamId}.create`);
    },

    close: async (streamId: string, _noWait?: RpcNoWait): Promise<void> => {
        debugLog?.log(`#${streamId}.close`);
        const opusDecoder = getDecoder(streamId, false);
        if (!opusDecoder)
            return;

        const decoder = opusDecoder.decoder;
        try {
            decoder.reset();
            await opusDecoder.end(true);
        }
        catch (e) {
            errorLog?.log(`#${streamId}.close: error while closing the decoder:`, e);
        }
        finally {
            decoder.reset();
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
        _noWait?: RpcNoWait,
    ): Promise<void> => {
        // debugLog?.log(`#${streamId}.onFrame`);
        getDecoder(streamId).decode(buffer, offset, length);
    },

    releaseBuffer: async(streamId: string, buffer: ArrayBuffer, _noWait?: RpcNoWait): Promise<void>  => {
        getDecoder(streamId).releaseBuffer(buffer);
    }
};

const server = rpcServer(`${logScope}.server`, worker, serverImpl);

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


