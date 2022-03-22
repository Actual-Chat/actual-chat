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

const LogScope: string = 'OpusDecoder'
/// #if MEM_LEAK_DETECTION
console.info(`${LogScope}: MEM_LEAK_DETECTION == true`);
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
    private readonly debug: boolean = false;
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
        return new OpusDecoder(decoder, workletPort);
    }

    public init(): void {
        console.assert(this.queue.length === 0, `${LogScope}.init: queue should be empty, check stop/reset logic`);
        this.state = 'waiting';
    }

    public pushData(data: ArrayBuffer): void {
        if (this.debug)
            console.debug(`${LogScope}.pushData: data size: ${data.byteLength} byte(s)`);
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', `${LogScope}.pushData: uninitialized but got data!`);
        console.assert(data.byteLength, `${LogScope}.pushData: got zero length data message!`);
        queue.push(data);
        this.processQueue();
    }

    public pushEnd(): void {
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', `${LogScope}.pushEnd: Uninitialized but got "end of data" message!`);
        queue.push('end');
        this.processQueue();
    }

    public stop(): void {
        const { queue } = this;
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
            const item = queue.shift();
            if (item === 'end') {
                if (debug) {
                    console.debug(`${LogScope}.processQueue: end is reached, sending end to worklet and stopping queue processing`);
                }
                // tell the worklet, that we are at the end of playing
                const msg: EndDecoderWorkerMessage = { type: 'end' };
                workletPort.postMessage(msg);
                this.stop();
                return;
            }

            const samples = this.decoder.decode(item);
            if (debug) {
                if (!!samples && samples.length > 0) {
                    console.debug(`${LogScope}.processQueue: opusDecode(${item.byteLength} bytes) `
                        + `returned ${samples.byteLength} `
                        + `bytes / ${samples.length} samples`);
                }
                else {
                    console.error(`${LogScope}.processQueue: opusDecode(${item.byteLength} bytes) `
                        + 'returned empty/unknown result');
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
            console.error(`${LogScope}.processQueue error:`, error);
        }
        finally {
            if (this.state === 'decoding')
                this.state = 'waiting';
        }

        this.processQueue();
    }
}
