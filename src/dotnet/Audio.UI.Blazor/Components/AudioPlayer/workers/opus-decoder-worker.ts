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

            const demuxerPromise = createDemuxer();
            const decoderPromise = createDecoder();
            const [dem, dec] = await Promise.all([demuxerPromise,decoderPromise]);
            demuxer = dem;
            decoder = dec;
            const readyToInitMessage: DecoderWorkerMessage = { topic: 'readyToInit' };
            worker.postMessage(readyToInitMessage);
            state = 'readyToInit';
            break;

        case 'init':
            const { header }: InitCommand = ev.data;
            await Promise.all([initDemuxer(), initDecoder()]);

            await demuxEnqueue(header);
            while (await demuxProcess()) {
                while (demuxer.audioPackets.length > 0) {
                    const { packet } = await demuxDequeue();
                    if (!isMetadataLoaded()) {
                        await decodeHeaderProcess(packet);
                    }
                }
            }

            const initCompletedMessage: DecoderWorkerMessage = { topic: 'initCompleted' };
            worker.postMessage(initCompletedMessage);
            state = 'decoding';
            break;

        case 'pushData':
            const { buffer }: PushDataCommand = ev.data;
            if (buffer.byteLength !== 0 && state === 'decoding') {
                queue.push(buffer);

                const _ = processQueue();
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

        // const workerMessage: DecoderWorkerMessage = { topic: "buffer", buffer: buffer };
        // worker.postMessage(workerMessage, [buffer]);

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
                    offset: offset,
                    length: length,
                };
                workletPort.postMessage(decoderMessage, [monoPcm.buffer]);
                // const playbackState = await this.feederNode.getState();
                // if (this._debugFeederStats) {
                //     this.log("Feeder stats: "
                //         + `playbackTime: ${playbackState.playbackTime}, `
                //         + `bufferedDuration: ${playbackState.bufferedDuration}`);
                // }
                // if (playbackState.bufferedDuration >= this._bufferEnoughThreshold) {
                //     if (!this._isPlaying) {
                //         if (this.onStartPlaying !== null)
                //             this.onStartPlaying();
                //         this.feederNode.play();
                //         this._isPlaying = true;
                //         self.setTimeout(this.onUpdateOffsetTick, this._updateOffsetMs);
                //         if (this._debugFeeder) {
                //             this.log("Feeder start playing");
                //         }
                //     }
                // }
                // we buffered enough data, tell to blazor about it and block the operation queue
                // if (playbackState.bufferedDuration >= this._bufferTooMuchThreshold) {
                //     await this.invokeOnChangeReadiness(false, playbackState.playbackTime, 4);
                //     const blocker = new Promise<void>(resolve => this._unblockQueue = resolve);
                //     this._queue.prepend({
                //         execute: () => blocker,
                //         onSuccess: () => {
                //             if (this._debugOperations)
                //                 this.logWarn("End blocking operation queue");
                //         },
                //         onStart: () => {
                //             if (this._debugOperations) {
                //                 this.logWarn("Start blocking operation queue, "
                //                     + `bufferedDuration: ${playbackState.bufferedDuration}`);
                //             }
                //         },
                //         onError: _ => {
                //         }
                //     });
                //
                //     const _ = this.onProcessingTick();
                // }
            }
        }
        // const buffers = encoder.flush();
        // const message: DecoderM = {
        //     command: 'encodedData',
        //     buffers
        // };
        // worker.postMessage(message, buffers);

    } catch (error) {
        isDecoding = false;
        throw error;
    } finally {
        isDecoding = false;
    }

    const _ = processQueue();
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
