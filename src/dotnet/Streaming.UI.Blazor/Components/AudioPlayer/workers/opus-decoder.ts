/// #if MEM_LEAK_DETECTION
import { Decoder } from '@actual-chat/codec/codec.debug';
/// #else
/// #code import { Decoder } from '@actual-chat/codec';
/// #endif
import { AsyncDisposable, Disposable } from 'disposable';
import { AsyncProcessor } from 'async-processor';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { FeederAudioWorklet } from '../worklets/feeder-audio-worklet-contract';
import { ObjectPool } from 'object-pool';
import { Log } from 'logging';
import { BufferHandler } from "./opus-decoder-worker-contract";

const { logScope, debugLog, warnLog, errorLog } = Log.get('OpusDecoder');
const enableFrequentDebugLog = false;

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

// Raw audio chunk consists of SAMPLES_PER_WINDOW floats
// 20ms * 48000Khz
const SAMPLES_PER_WINDOW = 960;
const SAMPLE_RATE = 48000;

const systemCodecConfig: AudioDecoderConfig = {
    codec: 'opus',
    numberOfChannels: 1,
    sampleRate: SAMPLE_RATE,
};


export class OpusDecoder implements BufferHandler, AsyncDisposable {
    private readonly streamId: string;
    private readonly processor: AsyncProcessor<ArrayBufferView | 'end'>;
    private readonly feederWorklet: FeederAudioWorklet & Disposable;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;
    private readonly largeBufferPool: ObjectPool<ArrayBuffer>;
    private mustAbort = false;
    private chunkTimeOffset = 0;

    private decoder: Decoder | null;
    private systemDecoder: AudioDecoder | null;

    public static async create(streamId: string, decoder: Decoder | null, feederNodePort: MessagePort): Promise<OpusDecoder> {
        return new OpusDecoder(streamId, decoder, feederNodePort);
    }

    /** accepts fully initialized decoder only, use the factory method `create` to construct an object */
    private constructor(streamId: string, decoder: Decoder | null, feederWorkletPort: MessagePort) {
        this.streamId = streamId;
        this.processor = new AsyncProcessor<Uint8Array | 'end'>('OpusDecoder', item => this.process(item));
        this.feederWorklet = rpcClientServer<FeederAudioWorklet>(`${logScope}.feederNode`, feederWorkletPort, this);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(SAMPLES_PER_WINDOW * 4)).expandTo(4);
        this.largeBufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(SAMPLES_PER_WINDOW * 4 * 3)).expandTo(2);
        this.decoder = decoder;
        if (!this.decoder) {
            // use system decoder
            this.systemDecoder = new AudioDecoder({
                error: this.onSystemDecoderError,
                output: this.onDecodedAudioChunk,
            });
            this.systemDecoder.configure(systemCodecConfig);
        }
    }

    public init(): void {
        this.mustAbort = false;
    }

    public async disposeAsync(): Promise<void> {
        if (this.processor.isRunning)
            await this.end(true);

        this.decoder?.delete();
        this.systemDecoder?.close();
        this.decoder = null;
        this.systemDecoder = null;
        this.mustAbort = true;
    }

    public decode(buffer: ArrayBuffer, offset: number, length: number,): void {
        warnLog?.assert(buffer.byteLength > 0, `#${this.streamId}.decode: got zero length buffer!`);
        const bufferView = new Uint8Array(buffer, offset, length);
        this.processor.enqueue(bufferView, false);
    }

    public async end(mustAbort: boolean): Promise<void> {
        debugLog?.log(`#${this.streamId}.end: mustAbort:`, mustAbort);
        if (!this.processor.isRunning) {
            // Special case: processor is already stopped by prev. 'end' command
            if (mustAbort && !this.mustAbort) {
                this.mustAbort = true;
                void this.feederWorklet.end(mustAbort, rpcNoWait);
            }
            return;
        }

        this.mustAbort ||= mustAbort;
        if (this.mustAbort)
            this.processor.clearQueue();
        this.processor.enqueue('end', false);
    }

    public async releaseBuffer(buffer: ArrayBuffer, _rpcNoWait: RpcNoWait): Promise<void> {
        if (buffer.byteLength <= SAMPLES_PER_WINDOW * 4)
            this.bufferPool.release(buffer);
        else
            this.largeBufferPool.release(buffer);
    }

    private async process(item: Uint8Array | 'end'): Promise<boolean> {
        try {
            if (item === 'end') {
                debugLog?.log(`#${this.streamId}.process: got 'end'`, this.mustAbort);
                void this.feederWorklet.end(this.mustAbort, rpcNoWait);
                return true;
            }

            if (this.systemDecoder) {
                const chunk = new EncodedAudioChunk({
                    data: item,
                    type: 'key',
                    duration: 20000, // 20ms
                    timestamp: this.chunkTimeOffset,
                });
                this.chunkTimeOffset += 20;
                this.systemDecoder.decode(chunk);
            }
            else {
                // typedViewSamples is a typed_memory_view to Decoder internal buffer - so you have to copy data
                const typedViewSamples = this.decoder.decode(item);
                if (typedViewSamples == null || typedViewSamples.length === 0) {
                    warnLog?.log(`#${this.streamId}.process: decoder returned empty result`);
                    return true;
                }

                const samplesBuffer = typedViewSamples.length == SAMPLES_PER_WINDOW
                    ? this.bufferPool.get()
                    : this.largeBufferPool.get();
                const samples = new Float32Array(samplesBuffer, 0, typedViewSamples.length);
                samples.set(typedViewSamples);

                if (enableFrequentDebugLog)
                    debugLog?.log(
                        `#${this.streamId}.process: decoded ${item.byteLength} byte(s) into ` +
                        `${samples.byteLength} byte(s) / ${samples.length} samples`);
                void this.feederWorklet.frame(samples.buffer, samples.byteOffset, samples.length, rpcNoWait);
            }
        }
        catch (e) {
            errorLog?.log(`#${this.streamId}.process: error:`, e);
        }
        // Keep running for reuse
        return true;
    }

    private onSystemDecoderError = (error: DOMException): void => {
        errorLog?.log(`onSystemDecoderError: `, error, this.streamId)
    }

    private onDecodedAudioChunk = (output: AudioData): void => {
        const samplesBuffer = output.numberOfFrames == SAMPLES_PER_WINDOW
            ? this.bufferPool.get()
            : this.largeBufferPool.get();
        const samples = new Float32Array(samplesBuffer, 0, output.numberOfFrames);
        output.copyTo(samples, { planeIndex: 0, format: 'f32-planar'})

        void this.feederWorklet.frame(samples.buffer, samples.byteOffset, samples.length, rpcNoWait);
    }
}
