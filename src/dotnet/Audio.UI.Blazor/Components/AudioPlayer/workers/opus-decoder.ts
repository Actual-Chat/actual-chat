import Denque from 'denque';
import { EndDecoderWorkerMessage, SamplesDecoderWorkerMessage } from './opus-decoder-worker-message';
/// #if MEM_LEAK_DETECTION
import codec, { Decoder, Codec } from '@actual-chat/codec/codec.debug';
import codecWasm from '@actual-chat/codec/codec.debug.wasm';
import codecWasmMap from '@actual-chat/codec/codec.debug.wasm.map';
/// #else
/// #code import codec, { Decoder, Codec } from '@actual-chat/codec';
/// #code import codecWasm from '@actual-chat/codec/codec.wasm';
/// #endif
import { Log, LogLevel, LogScope } from 'logging';
import 'logging-init';
import { getVersionedArtifactPath } from 'versioning';
import { delayAsync } from 'promises';

const LogScope: LogScope = 'OpusDecoder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

let codecModule: Codec | null = null;

function loadCodec(): Promise<void> {
    // wrapped promise to avoid exceptions with direct call to codec(...)
    return new Promise<void>((resolve,reject) => codec(getEmscriptenLoaderOptions())
        .then(
            val => {
                codecModule = val;
                self['codec'] = codecModule;
                resolve();
            },
            reason => reject(reason)));
}

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = getVersionedArtifactPath(codecWasm);
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

export class OpusDecoder {
    private readonly queue = new Denque<ArrayBuffer | 'end'>();
    private readonly decoder: Decoder;

    private readonly workletPort: MessagePort;
    private state: 'uninitialized' | 'waiting' | 'decoding' = 'uninitialized';

    /** accepts fully initialized decoder only, use the factory method `create` to construct an object */
    private constructor(decoder: Decoder, workletPort: MessagePort) {
        this.decoder = decoder;
        this.workletPort = workletPort;
    }

    public static async create(workletPort: MessagePort): Promise<OpusDecoder> {
        if (codecModule == null) {
            // Setting encoder module
            let retryCount = 0;
            let whenCodecModuleCreated = loadCodec();
            while (codecModule == null && retryCount++ < 3) {
                try {
                    await whenCodecModuleCreated;
                    break;
                }
                catch (e) {
                    warnLog.log(e, "error loading codec WASM module.")
                    await delayAsync(300);
                    whenCodecModuleCreated = loadCodec();
                }
            }
        }

        if (codecModule == null)
            throw new Error("Unable to load codec WASM module.");

        const decoder = new codecModule.Decoder();
        return new OpusDecoder(decoder, workletPort);
    }

    public init(): void {
        warnLog?.assert(this.queue.length === 0, `init: queue should be empty, check stop/reset logic`);
        this.state = 'waiting';
    }

    public pushData(data: ArrayBuffer): void {
        debugLog?.log(`pushData: data size: ${data.byteLength} byte(s)`);
        const { state, queue } = this;
        warnLog?.assert(state !== 'uninitialized', `pushData: uninitialized but got data!`);
        warnLog?.assert(data.byteLength > 0, `pushData: got zero length data message!`);
        queue.push(data);
        this.processQueue();
    }

    public pushEnd(): void {
        const { state, queue } = this;
        warnLog?.assert(state !== 'uninitialized', `pushEnd: Uninitialized but got "end of data" message!`);
        queue.push('end');
        this.processQueue();
    }

    public stop(): void {
        const { queue } = this;
        queue.clear();
        this.state = 'uninitialized';
    }

    private processQueue(): void {
        const { queue, workletPort } = this;

        if (this.state === 'decoding') {
            return;
        }

        try {
            this.state = 'decoding';
            while(true) {
                if (queue.isEmpty()) {
                    return;
                }

                const item = queue.shift();
                if (item === 'end') {
                    debugLog?.log(`processQueue: end is reached, sending end to worklet and stopping queue processing`);
                    // tell the worklet, that we are at the end of playing
                    const msg: EndDecoderWorkerMessage = { type: 'end' };
                    workletPort.postMessage(msg);
                    this.stop();
                    return;
                }

                const samples = this.decoder.decode(item);
                if (!!samples && samples.length > 0) {
                    debugLog?.log(
                        `processQueue: opusDecode(${item.byteLength} bytes) `
                        + `returned ${samples.byteLength} `
                        + `bytes / ${samples.length} samples`);
                }
                else {
                    errorLog?.log(`processQueue: opusDecode(${item.byteLength} bytes) returned empty/unknown result`);
                }

                if (samples == null || samples.length === 0)
                    return;

                const msg: SamplesDecoderWorkerMessage = {
                    type: 'samples',
                    buffer: samples,
                    length: samples.byteLength,
                    offset: samples.byteOffset,
                };
                workletPort.postMessage(msg, [samples.buffer]);
            }
        }
        catch (error) {
            errorLog?.log(`processQueue: unhandled error:`, error);
        }
        finally {
            if (this.state === 'decoding')
                this.state = 'waiting';
        }
    }
}
