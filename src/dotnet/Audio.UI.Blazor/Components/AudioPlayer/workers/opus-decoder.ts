import Denque from 'denque';
import OGVDecoderAudioOpusW from 'ogv/dist/ogv-decoder-audio-opus-wasm';
import OGVDecoderAudioOpusWWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OGVDemuxerWebMW from 'ogv/dist/ogv-demuxer-webm-wasm';
import OGVDemuxerWebMWWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';
import { EndDecoderWorkerMessage, SamplesDecoderWorkerMessage } from "./opus-decoder-worker-message";

let demuxerWasmBinary: ArrayBuffer | null = null;
let decoderWasmBinary: ArrayBuffer | null = null;

export class OpusDecoder {
    private readonly debug: boolean = false;
    private readonly queue = new Denque<ArrayBuffer | 'end'>();
    private readonly demuxer: Demuxer;
    private readonly decoder: Decoder;

    private readonly workletPort: MessagePort;
    private state: 'uninitialized' | 'waiting' | 'decoding' = 'uninitialized';

    /** accepts fully initialized demuxer/decoder only, use the factory method `create` to construct an object */
    private constructor(demuxer: Demuxer, decoder: Decoder, workletPort: MessagePort) {
        this.demuxer = demuxer;
        this.decoder = decoder;
        this.workletPort = workletPort;
    }

    public static async create(workletPort: MessagePort): Promise<OpusDecoder> {
        const demuxerPromise = OpusDecoder.createDemuxer();
        const decoderPromise = OpusDecoder.createDecoder();
        const [demuxer, decoder] = await Promise.all([demuxerPromise, decoderPromise]);
        return new OpusDecoder(demuxer, decoder, workletPort);
    }

    public async init(header: ArrayBuffer): Promise<void> {
        console.assert(this.queue.length === 0, 'queue should be empty, check stop/reset logic');
        console.assert(this.demuxer.audioPackets.length === 0, 'demuxer should be empty, check stop/reset logic');
        await this.demuxEnqueue(header);
        while (await this.demuxProcess()) {
            while (this.demuxer.audioPackets.length > 0) {
                const { packet } = await this.demuxDequeue();
                await this.decodeHeaderProcess(packet);
            }
        }
        this.state = 'waiting';
    }

    public pushData(data: ArrayBuffer): void {
        // if (this.debug)
        //     console.debug(`Decoder: push data bytes: ${data.byteLength}`);
        const { state, queue } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got a data. Lifetime error.");
        console.assert(data.byteLength, "Decoder got an empty data message.");
        queue.push(data);
        const _ = this.processQueue();
    }

    public pushEnd(): void {
        const { state, queue, debug } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got an end of data. Lifetime error.");
        queue.push('end');
        const _ = this.processQueue();
    }

    public async stop(): Promise<void> {
        const { demuxer, state, queue, debug } = this;
        console.assert(state !== 'uninitialized', "Decoder isn't initialized but got stop message. Lifetime error.");
        queue.clear();
        demuxer.audioPackets = [];
        await this.flushDemuxer();
        // free + alloc the queue in the demuxer
        this.demuxer["_ogv_demuxer_destroy"]();
        await new Promise<void>(resolve => this.demuxer.init(resolve));
        this.state = 'uninitialized';
    }

    private get isMetadataLoaded(): boolean {
        return this.demuxer.loadedMetadata !== null
            && this.demuxer.loadedMetadata !== false
            && this.demuxer.audioCodec !== undefined
            && this.demuxer.audioCodec !== null
            && this.decoder.audioFormat !== null
            && this.decoder.audioFormat !== undefined;
    }

    private async processQueue(): Promise<void> {
        const { queue, demuxer, workletPort, debug } = this;
        if (queue.isEmpty() || this.state === 'decoding') {
            return;
        }

        try {
            this.state = 'decoding';
            const queueItem = queue.pop();
            if (queueItem === 'end') {
                if (debug) {
                    console.debug("Decoder: queue end is reached. Send end to worklet and stop queue processing");
                }
                // tell the worklet, that we are at the end of playing
                const msg: EndDecoderWorkerMessage = { type: 'end' };
                workletPort.postMessage(msg);
                await this.stop();
                return;
            }
            await this.demuxEnqueue(queueItem);
            while (await this.demuxProcess()) {
                while (demuxer.audioPackets.length > 0) {

                    const { packet, discardPadding: padding } = await this.demuxDequeue();
                    // if we haven't parsed metadata yet
                    if (!this.isMetadataLoaded) {
                        await this.decodeHeaderProcess(packet);
                    }
                    const samples = await this.decodeProcess(packet);
                    if (debug) {
                        if (samples !== null && samples.length > 0) {
                            console.debug(`Decoder: decodeProcess(${packet.byteLength} bytes, padding:${padding}) `
                                + `returned ${samples[0].byteLength} `
                                + `bytes / ${samples[0].length} samples, `
                                + `isMetadataLoaded: ${this.isMetadataLoaded}`);
                        }
                        else {
                            console.debug(`Decoder: decodeProcess(${packet.byteLength} bytes, padding: ${padding}) ` +
                                "returned null");
                        }
                    }
                    if (samples == null)
                        continue;

                    let channel = samples[0];
                    const msg: SamplesDecoderWorkerMessage = {
                        type: 'samples',
                        buffer: channel.buffer,
                        length: channel.byteLength,
                        offset: channel.byteOffset,
                    };
                    workletPort.postMessage(msg, [channel.buffer]);
                }
            }
        }
        catch (error) {
            console.error(error);
        }
        finally {
            this.state = 'waiting';
        }

        const _ = this.processQueue();
    }

    private decodeProcess(packet: ArrayBuffer): Promise<Float32Array[] | null> {
        const { decoder } = this;
        return decoder.processing === true
            ? Promise.reject("Decoder is processing")
            : new Promise<Float32Array[] | null>((resolve, reject) => {
                decoder.processAudio(packet, _ => {
                    if (decoder.audioBuffer !== null && decoder.audioBuffer.length > 0)
                        resolve(decoder.audioBuffer);
                    else
                        reject("Can't decode packet to the right format");
                });
            });
    }

    private decodeHeaderProcess(packet: ArrayBuffer): Promise<void> {
        const { decoder } = this;
        return decoder.processing === true
            ? Promise.reject("Decoder is processing")
            : new Promise<void>(resolve => decoder.processHeader(packet, _ => resolve()));
    }

    private demuxDequeue(): Promise<{ packet: ArrayBuffer; discardPadding: number; }> {
        const { demuxer } = this;
        return new Promise<{ packet: ArrayBuffer; discardPadding: number; }>(resolve =>
            demuxer.dequeueAudioPacket((packet, discardPadding) => resolve({ packet, discardPadding })));
    }

    private demuxEnqueue(buffer: ArrayBuffer): Promise<void> {
        const { demuxer } = this;
        return new Promise(resolve => demuxer.receiveInput(buffer, resolve));
    }

    private demuxProcess(): Promise<boolean> {
        const { demuxer } = this;
        return demuxer.processing === true
            ? Promise.reject("Demuxer is processing")
            : new Promise<boolean>(resolve => demuxer.process(more => resolve(more)));
    }

    private flushDemuxer(): Promise<void> {
        return new Promise(resolve => {
            this.demuxer.flush(resolve);
        });
    }

    private static getEmscriptenLoaderOptions(wasmBinary: ArrayBuffer): EmscriptenLoaderOptions {
        return {
            locateFile: (filename: string) => {
                if (filename === "ogv-demuxer-webm-wasm.wasm")
                    return OGVDemuxerWebMWWasm;
                else if (filename === "ogv-decoder-audio-opus-wasm.wasm")
                    return OGVDecoderAudioOpusWWasm;
                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
                // emscripten 1.37.25 loads memory initializers as data: URI
                else if (filename.slice(0, 5) === 'data:')
                    return filename;
                else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
            },
            /**
             * Pre-loaded WASM binary as ArrayBuffer, prevents multiple http calls
             * https://emscripten.org/docs/compiling/WebAssembly.html?highlight=wasmbinary#wasm-files-and-compilation
             */
            wasmBinary: wasmBinary,
        };
    }

    private static async createDemuxer(): Promise<Demuxer> {
        if (demuxerWasmBinary === null) {
            const path = OGVDemuxerWebMWWasm;
            const response = await fetch(path);
            demuxerWasmBinary = await response.arrayBuffer();
        }

        const demuxer = await (OGVDemuxerWebMW(OpusDecoder.getEmscriptenLoaderOptions(demuxerWasmBinary)) as Promise<Demuxer>);
        await new Promise<void>(resolve => demuxer.init(resolve));
        return demuxer;
    }

    private static async createDecoder(): Promise<Decoder> {
        if (decoderWasmBinary === null) {
            const path = OGVDecoderAudioOpusWWasm;
            const response = await fetch(path);
            decoderWasmBinary = await response.arrayBuffer();
        }
        const decoder = await (OGVDecoderAudioOpusW(OpusDecoder.getEmscriptenLoaderOptions(decoderWasmBinary)) as Promise<Decoder>);
        await new Promise<void>(resolve => decoder.init(resolve));
        return decoder;
    }
}
