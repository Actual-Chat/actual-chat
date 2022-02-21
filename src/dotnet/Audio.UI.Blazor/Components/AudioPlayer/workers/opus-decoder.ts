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

/// #if MEM_LEAK_DETECTION
console.info('MEM_LEAK_DETECTION == true');
/// #endif

let codecModule: Codec | null = null;
const codecModuleReady = codec(getEmscriptenLoaderOptions()).then(val => {
    codecModule = val;
    self['codec'] = codecModule;
});

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            if (filename.slice(-4) === 'wasm')
                return codecWasm;
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
    private readonly debug: boolean = true;
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
            await codecModuleReady;
        }
        const decoder = new codecModule.Decoder();
        console.warn('create', decoder);
        return new OpusDecoder(decoder, workletPort);
    }

    public init(header: ArrayBuffer): Promise<void> {
        console.log('init', header);
        console.assert(this.queue.length === 0, 'queue should be empty, check stop/reset logic');
        // await this.decodeHeaderProcess(header);
        // TODO: should call usual processing ? /no/
        this.state = 'waiting';
        return Promise.resolve();
    }

    public pushData(data: ArrayBuffer): void {
        if (this.debug)
            console.debug(`Decoder: push data bytes: ${data.byteLength}`);
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got a data. Lifetime error.");
        console.assert(data.byteLength, 'Decoder got an empty data message.');
        queue.push(data);
        this.processQueue();
    }

    public pushEnd(): void {
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got an end of data. Lifetime error.");
        queue.push('end');
        this.processQueue();
    }

    public stop(): void {
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got stop message. Lifetime error.");
        queue.clear();
        this.state = 'uninitialized';
    }

    private processQueue(): void {
        const { queue, workletPort, debug } = this;
        if (queue.isEmpty() || this.state === 'decoding') {
            return;
        }

        try {
            this.state = 'decoding';
            const queueItem = queue.pop();
            if (queueItem === 'end') {
                if (debug) {
                    console.debug('Decoder: queue end is reached. Send end to worklet and stop queue processing');
                }
                // tell the worklet, that we are at the end of playing
                const msg: EndDecoderWorkerMessage = { type: 'end' };
                workletPort.postMessage(msg);
                this.stop();
                return;
            }

            const samples = this.decoder.decode(queueItem);
            if (debug) {
                if (!!samples && samples.length > 0) {
                    console.debug(`Decoder: opusDecode(${queueItem.byteLength} bytes) `
                        + `returned ${samples.byteLength} `
                        + `bytes / ${samples.length} samples`);
                }
                else {
                    console.error(`Decoder: opusDecode(${queueItem.byteLength} bytes) ` +
                        'returned empty/unknown result');
                }
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
        catch (error) {
            console.error(error);
        }
        finally {
            this.state = 'waiting';
        }

        this.processQueue();
    }
}
