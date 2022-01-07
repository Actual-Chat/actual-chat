import OGVDecoderAudioOpusW from 'ogv/dist/ogv-decoder-audio-opus-wasm';
import OGVDecoderAudioOpusWWasm from 'ogv/dist/ogv-decoder-audio-opus-wasm.wasm';
import OGVDemuxerWebMW from 'ogv/dist/ogv-demuxer-webm-wasm';
import OGVDemuxerWebMWWasm from 'ogv/dist/ogv-demuxer-webm-wasm.wasm';
import Denque from 'denque';
import {
    DecoderCommand,
    DecoderMessage,
    DecoderWorkerMessage,
    DecoderWorkletMessage,
    InitCommand,
    PushDataCommand, StopCommand
} from "./opus-decoder-worker-message";

type WorkerState = 'inactive'|'readyToInit'|'decoding'|'closed';

/** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
const BufferTooMuchThreshold = 20.0;
const SampleRate = 48000;

const queue = new Denque<ArrayBuffer | 'endOfStream'>();
const worker = self as unknown as Worker;
let state: WorkerState = 'inactive';
let workletPort: MessagePort = null;
let demuxer: Demuxer = null;
let decoder: Decoder = null;
let isDecoding: boolean = false;

worker.onmessage = async (ev: MessageEvent) => {
    const { command }: DecoderCommand = ev.data;
    switch (command) {
        case 'loadDecoder':
            workletPort = ev.ports[0];
            workletPort.onmessage = onWorkletMessage;
            await loadDecoder();
            state = 'readyToInit';
            break;

        case 'init': {
                const { buffer, offset, length }: InitCommand = ev.data;
                await init(buffer, offset, length);
                state = 'decoding';
            }
            break;

        case 'pushData': {
                const { buffer, offset, length }: PushDataCommand = ev.data;
                if (buffer.byteLength !== 0 && state === 'decoding') {
                    const data = buffer.slice(offset, offset + length);
                    queue.push(data);

                    const _ = processQueue();
                }
            }
            break;

        case 'endOfStream':
            queue.push('endOfStream');
            break;

        case 'stop':
            await flushDemuxer();

            destroyDemuxer();
            destroyDecoder();
            state = 'readyToInit';
            break;
    }

};

const onWorkletMessage = async (ev: MessageEvent<DecoderWorkletMessage>) => {
    // do nothing, we just receive buffer as transferable there for GC
};

async function processQueue(): Promise<void> {
    if (queue.isEmpty()) {
        return;
    }

    if (isDecoding) {
        return;
    }

    try {
        isDecoding = true;

        const queueItem = queue.pop();
        if (queueItem == 'endOfStream') {
            await flushDemuxer();

            destroyDemuxer();
            destroyDecoder();
            state = 'readyToInit';

            return;
        }

        await demuxEnqueue(queueItem);

        while (await demuxProcess()) {
            while (demuxer.audioPackets.length > 0) {

                const {packet, discardPadding } = await demuxDequeue();
                // if we haven't parsed metadata yet
                if (!isMetadataLoaded()) {
                    await decodeHeaderProcess(packet);
                    continue;
                }

                const samples = await decodeProcess(packet);
                if (samples === null)
                    continue;

                let monoPcm = samples[0];
                let offset = 0;
                let length = monoPcm.length;

                if (discardPadding) {
                    // discardPadding is in nanoseconds
                    // negative value trims from beginning
                    // positive value trims from end
                    let trim = Math.round(discardPadding * SampleRate / 1000000000);
                    if (trim > 0) {
                        length = monoPcm.length - Math.min(trim, monoPcm.length);
                        monoPcm = monoPcm.subarray(0, length);
                    } else {
                        offset = Math.min(Math.abs(trim), monoPcm.length);
                        length = monoPcm.length - offset;
                        monoPcm = monoPcm.subarray(offset, length);
                    }
                }

                // if (this._debugDecoder) {
                //     if (samples !== null && samples.length > 0) {
                //         this.log(`decodeProcess(${packet.byteLength} bytes, padding:${padding}) `
                //             + `returned ${samples[0].byteLength} `
                //             + `bytes / ${samples[0].length} samples, `
                //             + `isMetadataLoaded: ${this._isMetadataLoaded}`);
                //     } else {
                //         this.log(`decodeProcess(${packet.byteLength} bytes, padding: ${padding}) ` +
                //             "returned null");
                //     }
                // }

                const decoderMessage: DecoderMessage = {
                    topic: 'samples',
                    buffer: monoPcm.buffer,
                    offset: offset * 4,
                    length: length * 4,
                };
                workletPort.postMessage(decoderMessage, [monoPcm.buffer]);

            }
        }
    } catch (error) {
        isDecoding = false;
        console.error(error);
        throw error;

    } finally {
        isDecoding = false;
    }

    const _ = processQueue();
}

async function init(headerBuffer: ArrayBuffer, offset: number, length: number): Promise<void> {
    try {
        await Promise.all([initDemuxer(), initDecoder()]);

        const header = headerBuffer.slice(offset, offset + length);
        await demuxEnqueue(header);
        while (await demuxProcess()) {
            while (demuxer.audioPackets.length > 0) {
                const {packet} = await demuxDequeue();
                if (!isMetadataLoaded()) {
                    await decodeHeaderProcess(packet);
                }
            }
        }

        const initCompletedMessage: DecoderWorkerMessage = {topic: 'initCompleted'};
        worker.postMessage(initCompletedMessage);
    }
    catch (error) {
        console.error(error);
        throw error;
    }
}

async function loadDecoder(): Promise<void> {
    try {
        const demuxerPromise = createDemuxer();
        const decoderPromise = createDecoder();
        const [dem, dec] = await Promise.all([demuxerPromise,decoderPromise]);
        demuxer = dem;
        decoder = dec;
        const readyToInitMessage: DecoderWorkerMessage = { topic: 'readyToInit' };
        worker.postMessage(readyToInitMessage);
    }
    catch (error) {
        console.error(error);
        throw error;
    }
}

function isMetadataLoaded(): boolean {
    return demuxer.loadedMetadata !== null
        && demuxer.loadedMetadata !== false
        && demuxer.audioCodec !== undefined
        && demuxer.audioCodec !== null
        && decoder.audioFormat !== null
        && decoder.audioFormat !== undefined;
}

function decodeProcess(packet: ArrayBuffer): Promise<Float32Array[] | null> {
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

function decodeHeaderProcess(packet: ArrayBuffer): Promise<void> {
    return decoder.processing === true
        ? Promise.reject("Decoder is processing")
        : new Promise<void>(resolve => decoder.processHeader(packet, _ => resolve()));
}

function demuxDequeue(): Promise<{ packet: ArrayBuffer; discardPadding: number; }> {
    return new Promise<{ packet: ArrayBuffer; discardPadding: number; }>(resolve =>
        demuxer.dequeueAudioPacket((packet, discardPadding) => resolve({ packet, discardPadding })));
}

function demuxEnqueue(buffer: ArrayBuffer): Promise<void> {
    return new Promise(resolve => demuxer.receiveInput(buffer, resolve));
}

function demuxProcess(): Promise<boolean> {
    return demuxer.processing === true
        ? Promise.reject("Demuxer is processing")
        : new Promise<boolean>(resolve => demuxer.process(more => resolve(more)));
}

function getEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
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
        }
    };
}

/** each time loads OGVDemuxerWebMWWasm with HTTP call, at least until it's cached by browser */
function createDemuxer(): Promise<Demuxer> {
    return OGVDemuxerWebMW(getEmscriptenLoaderOptions()) as Promise<Demuxer>;
}

/** each time loads OGVDecoderAudioOpusWWasm with HTTP call, at least until it's cached by browser */
function createDecoder():  Promise<Decoder> {
    return OGVDecoderAudioOpusW(getEmscriptenLoaderOptions()) as Promise<Decoder>;
}

function flushDemuxer(): Promise<void> {
    return new Promise(resolve => {
        demuxer.flush(resolve);
    })
}

function initDemuxer(): Promise<void> {
    return new Promise(resolve => {
        demuxer.init(resolve);
    })
}

function initDecoder(): Promise<void> {
    return new Promise(resolve => {
        decoder.init(resolve);
    })
}

function destroyDemuxer(): void {
    demuxer["_ogv_demuxer_destroy"]();
}

function destroyDecoder(): void {
    decoder["_ogv_audio_decoder_destroy"]();
}


let getTimestamp;
if (typeof performance === 'undefined' || typeof performance.now === 'undefined') {
    getTimestamp = Date.now;
} else {
    getTimestamp = performance.now.bind(performance);
}
