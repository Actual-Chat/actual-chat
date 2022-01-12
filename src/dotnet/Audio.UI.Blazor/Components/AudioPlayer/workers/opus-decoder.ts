import Denque from 'denque';
import OGVDecoderAudioOpusW from 'ogv/dist/ogv-decoder-audio-opus-wasm';
import OGVDecoderAudioOpusWWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OGVDemuxerWebMW from 'ogv/dist/ogv-demuxer-webm-wasm';
import OGVDemuxerWebMWWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';
import { SamplesDecoderWorkerMessage } from "./opus-decoder-worker-message";

type DecoderState = 'inactive' | 'waiting' | 'decoding';

const SAMPLE_RATE = 48000;
let demuxerWasmBinary: ArrayBuffer = null;
let decoderWasmBinary: ArrayBuffer = null;

export class OpusDecoder {
    private readonly queue = new Denque<ArrayBuffer | 'end'>();
    private readonly demuxer: Demuxer;
    private readonly decoder: Decoder;
    private readonly release: (decoder: OpusDecoder) => void;

    private _playerId: string;
    private workletPort: MessagePort;
    private state: DecoderState = 'inactive';

    public get playerId() {
        return this._playerId;
    }

    constructor(release: (decoder: OpusDecoder) => void, demuxer: Demuxer, decoder: Decoder) {
        this.release = release;
        this.demuxer = demuxer;
        this.decoder = decoder;
    }

    public static async create(release: (decoder: OpusDecoder) => void): Promise<OpusDecoder> {
        const demuxerPromise = OpusDecoder.createDemuxer();
        const decoderPromise = OpusDecoder.createDecoder();
        const [demuxer, decoder] = await Promise.all([demuxerPromise,decoderPromise]);
        return new OpusDecoder(release, demuxer, decoder);
    }

    public async init(playerId: string, workletPort: MessagePort, buffer:ArrayBuffer, offset: number, length: number) : Promise<void> {
        try {
            this._playerId = playerId;
            this.workletPort = workletPort;

            await Promise.all([this.initDemuxer(), this.initDecoder()]);

            const header = buffer.slice(offset, offset + length);
            await this.demuxEnqueue(header);
            while (await this.demuxProcess()) {
                while (this.demuxer.audioPackets.length > 0) {
                    const {packet} = await this.demuxDequeue();
                    if (!this.isMetadataLoaded) {
                        await this.decodeHeaderProcess(packet);
                    }
                }
            }
            this.state = 'waiting';
        }
        catch (error) {
            console.error(error);
            throw error;
        }
    }

    public pushData(buffer:ArrayBuffer, offset: number, length: number): void {
        const { state, queue } = this;
        if (buffer.byteLength !== 0 && state !== 'inactive') {
            const data = buffer.slice(offset, offset + length);
            queue.push(data);

            const _ = this.processQueue();
        }
    }

    public pushEndOfStream(): void {
        this.queue.push('end');

        const _ = this.processQueue();
    }

    public async stop(): Promise<void> {
        await this.flushDemuxer();

        this.destroyDemuxer();
        this.destroyDecoder();
        this.state = 'inactive';

        this.release(this);
        this._playerId = null;
        this.workletPort = null;
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
        const { queue, demuxer, workletPort } = this;

        if (queue.isEmpty()) {
            return;
        }

        if (this.state === 'decoding') {
            return;
        }

        try {
            this.state = 'decoding';

            const queueItem = queue.pop();
            if (queueItem == 'end') {
                await this.stop();
                return;
            }

            await this.demuxEnqueue(queueItem);

            while (await this.demuxProcess()) {
                while (demuxer.audioPackets.length > 0) {

                    const {packet, discardPadding } = await this.demuxDequeue();
                    // if we haven't parsed metadata yet
                    if (!this.isMetadataLoaded) {
                        await this.decodeHeaderProcess(packet);
                        continue;
                    }

                    const samples = await this.decodeProcess(packet);
                    if (samples === null)
                        continue;

                    let monoPcm = samples[0];
                    let offset = 0;
                    let length = monoPcm.length;

                    if (discardPadding) {
                        // discardPadding is in nanoseconds
                        // negative value trims from beginning
                        // positive value trims from end
                        let trim = Math.round(discardPadding * SAMPLE_RATE / 1000000000);
                        if (trim > 0) {
                            length = monoPcm.length - Math.min(trim, monoPcm.length);
                            monoPcm = monoPcm.subarray(0, length);
                        } else {
                            offset = Math.min(Math.abs(trim), monoPcm.length);
                            length = monoPcm.length - offset;
                            monoPcm = monoPcm.subarray(offset, length);
                        }
                    }

                    const msg: SamplesDecoderWorkerMessage = {
                        type: 'samples',
                        buffer: monoPcm.buffer,
                        offset: offset * 4,
                        length: length * 4,
                    };
                    workletPort.postMessage(msg, [monoPcm.buffer]);

                }
            }
        } catch (error) {
            console.error(error);
            throw error;

        } finally {
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
        })
    }

    private initDemuxer(): Promise<void> {
        return new Promise(resolve => {
            this.demuxer.init(resolve);
        })
    }

    private initDecoder(): Promise<void> {
        return new Promise(resolve => {
            this.decoder.init(resolve);
        })
    }

    private destroyDemuxer(): void {
        this.demuxer["_ogv_demuxer_destroy"]();
    }

    private destroyDecoder(): void {
        this.decoder["_ogv_audio_decoder_destroy"]();
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
            // ** Pre-loaded WASM binary as ArrayBuffer
            // * https://emscripten.org/docs/compiling/WebAssembly.html?highlight=wasmbinary#wasm-files-and-compilation
            // * /
            wasmBinary: wasmBinary,
        };
    }

    /** each time loads OGVDemuxerWebMWWasm with HTTP call, at least until it's cached by browser */
    private static async createDemuxer(): Promise<Demuxer> {
        if (demuxerWasmBinary == null) {
            const path = OGVDemuxerWebMWWasm;
            const response = await fetch(path);
            demuxerWasmBinary = await response.arrayBuffer();
        }

        return await (OGVDemuxerWebMW(OpusDecoder.getEmscriptenLoaderOptions(demuxerWasmBinary)) as Promise<Demuxer>);
    }

    /** each time loads OGVDecoderAudioOpusWWasm with HTTP call, at least until it's cached by browser */
    private static async createDecoder():  Promise<Decoder> {
        if (decoderWasmBinary == null) {
            const path = OGVDecoderAudioOpusWWasm;
            const response = await fetch(path);
            decoderWasmBinary = await response.arrayBuffer();
        }

        return OGVDecoderAudioOpusW(OpusDecoder.getEmscriptenLoaderOptions(decoderWasmBinary)) as Promise<Decoder>;
    }
}
