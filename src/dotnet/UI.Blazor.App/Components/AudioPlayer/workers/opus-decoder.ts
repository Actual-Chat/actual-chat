// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await */
/// #if MEM_LEAK_DETECTION
import { Decoder } from '@actual-chat/codec/codec.debug';
/// #else
/// #code import { Decoder } from '@actual-chat/codec';
/// #endif

import { AUDIO } from 'app-constants';
import { AsyncDisposable, Disposable } from 'disposable';
import { AsyncProcessor } from 'async-processor';
import { rpcClient, rpcClientServer, RpcNoWait, rpcNoWait } from 'rpc';
import { FeederAudioWorklet } from '../worklets/feeder-audio-worklet-contract';
import { ObjectPool } from 'object-pool';
import { getLogs } from 'logging';
import { BufferHandler } from './opus-decoder-worker-contract';
import Denque from 'denque';

const { logScope, debugLog, warnLog, errorLog } = getLogs('OpusDecoder');
const enableFrequentDebugLog = false;

interface EncodedFrame {
    data: Uint8Array;
    sourceOffsetMs: number;
}

interface DecodeTiming {
    sourceOffsetMs: number;
}

class EncodedFrameBuffer {
    private readonly frames = new Denque<EncodedFrame | 'end'>();
    private targetDurationMs = 0;
    private primed = false;

    get length(): number { return this.frames.length; }

    setTargetDuration(targetDurationMs: number): void {
        this.targetDurationMs = Math.max(0, targetDurationMs);
    }

    push(frame: EncodedFrame): void {
        this.frames.push(frame);
    }

    end(): void {
        this.frames.push('end');
    }

    clear(): void {
        this.frames.clear();
        this.primed = false;
    }

    shiftReady(): EncodedFrame | 'end' | undefined {
        const frame = this.frames.peekFront();
        if (!frame)
            return undefined;
        if (frame === 'end')
            return this.frames.shift();
        if (!this.canRelease())
            return undefined;

        return this.frames.shift();
    }

    private canRelease(): boolean {
        if (this.targetDurationMs <= 0 || this.hasEnd())
            return true;
        // Prebuffer once: gate the start until we first reach target, then
        // release on demand. Re-gating below target mid-stream would strand
        // the cushion during an arrival gap and starve the feeder (clicks);
        // letting it drain is what absorbs jitter. Re-primes on clear().
        if (this.primed)
            return true;
        if (this.durationMs() >= this.targetDurationMs) {
            this.primed = true;
            return true;
        }
        return false;
    }

    private durationMs(): number {
        const first = this.firstFrame();
        const last = this.lastFrame();
        if (!first || !last)
            return 0;

        return Math.max(0, last.sourceOffsetMs + AUDIO.frameDurationMs - first.sourceOffsetMs);
    }

    private hasEnd(): boolean {
        return this.frames.peekBack() === 'end';
    }

    private firstFrame(): EncodedFrame | undefined {
        for (let i = 0; i < this.frames.length; i++) {
            const frame = this.frames.peekAt(i);
            if (frame && frame !== 'end')
                return frame;
        }
        return undefined;
    }

    private lastFrame(): EncodedFrame | undefined {
        for (let i = this.frames.length - 1; i >= 0; i--) {
            const frame = this.frames.peekAt(i);
            if (frame && frame !== 'end')
                return frame;
        }
        return undefined;
    }
}

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

export class OpusDecoder implements BufferHandler, AsyncDisposable {
    private readonly streamId: string;
    private readonly processor: AsyncProcessor<EncodedFrame | 'end'>;
    private readonly feederWorklet: FeederAudioWorklet & Disposable;
    private readonly bufferPool: ObjectPool<ArrayBuffer>;
    private readonly largeBufferPool: ObjectPool<ArrayBuffer>;
    private readonly encodedFrames = new EncodedFrameBuffer();
    private readonly systemDecodeTimings = new Denque<DecodeTiming>();
    private mustAbort = false;
    private frameRequested = false;
    private chunkTimeOffset = 0;
    private sourceRecordedAtMs = 0;

    private decoder: Decoder | null;
    private systemDecoder: AudioDecoder | null;

    public static async create(streamId: string, decoder: Decoder | null, feederNodePort: MessagePort): Promise<OpusDecoder> {
        return new OpusDecoder(streamId, decoder, feederNodePort);
    }

    /** accepts fully initialized decoder only, use the factory method `create` to construct an object */
    private constructor(streamId: string, decoder: Decoder | null, feederWorkletPort: MessagePort) {
        this.streamId = streamId;
        this.processor = new AsyncProcessor<EncodedFrame | 'end'>('OpusDecoder', item => this.process(item));
        this.feederWorklet = rpcClientServer<FeederAudioWorklet>(`${logScope}.feederNode`, feederWorkletPort, this);
        this.bufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(AUDIO.play.samplesPerWindow * 4)).expandTo(4);
        this.largeBufferPool = new ObjectPool<ArrayBuffer>(() => new ArrayBuffer(AUDIO.play.samplesPerWindow * 4 * 3)).expandTo(2);
        this.decoder = decoder;
        if (!this.decoder) {
            // use system decoder
            this.systemDecoder = new AudioDecoder({
                error: this.onSystemDecoderError,
                output: this.onDecodedAudioChunk,
            });
            this.systemDecoder.configure({
                codec: 'opus',
                numberOfChannels: 1,
                sampleRate: AUDIO.play.sampleRate,
            });
        }
    }

    public init(sourceRecordedAtMs = this.sourceRecordedAtMs): void {
        this.mustAbort = false;
        this.frameRequested = false;
        this.chunkTimeOffset = 0;
        this.sourceRecordedAtMs = sourceRecordedAtMs;
        this.encodedFrames.clear();
        this.systemDecodeTimings.clear();
        this.processor.clearQueue();
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

    public decode(buffer: ArrayBuffer, offset: number, length: number, sourceOffsetMs: number): void {
        warnLog?.assert(buffer.byteLength > 0, `#${this.streamId}.decode: got zero length buffer!`);
        const bufferView = new Uint8Array(buffer, offset, length);
        this.encodedFrames.push({ data: bufferView, sourceOffsetMs });
        this.flushDecodeDemand();
    }

    /**
     * Sizes the encoded (pre-decoder) buffer from the total target playback delay:
     *   encoded = max(MinEncodeBufferSize, target - DefaultDecodedBufferSize - DefaultAudioEnginePlaybackLatency)
     */
    public setTargetBufferSize(targetBufferSizeMs: number): void {
        const minEncoded = AUDIO.play.minEncodedBufferSizeMs;
        const decoded = AUDIO.play.decodedBufferSizeMs;
        const engineLatency = AUDIO.play.audioEnginePlaybackLatencyMs;
        const encoded = Math.max(minEncoded, targetBufferSizeMs - decoded - engineLatency);
        this.encodedFrames.setTargetDuration(encoded);
        this.flushDecodeDemand();
    }

    public async end(mustAbort: boolean): Promise<void> {
        debugLog?.log(`#${this.streamId}.end: mustAbort:`, mustAbort);
        if (mustAbort) {
            this.mustAbort = true;
            this.frameRequested = false;
            this.encodedFrames.clear();
            this.systemDecodeTimings.clear();
            this.processor.clearQueue();
            void this.feederWorklet.end(true, rpcNoWait);
            return;
        }

        this.encodedFrames.end();
        this.flushDecodeDemand();
    }

    public async requestFrame(_noWait?: RpcNoWait): Promise<void> {
        if (this.mustAbort)
            return;

        this.frameRequested = true;
        this.flushDecodeDemand();
    }

    public async releaseBuffer(buffer: ArrayBuffer, _rpcNoWait?: RpcNoWait): Promise<void> {
        if (buffer.byteLength <= AUDIO.play.samplesPerWindow * 4)
            this.bufferPool.release(buffer);
        else
            this.largeBufferPool.release(buffer);
    }

    private async process(item: EncodedFrame | 'end'): Promise<boolean> {
        try {
            if (item === 'end') {
                debugLog?.log(`#${this.streamId}.process: got 'end'`, this.mustAbort);
                // A rejecting flush - what a closed AudioDecoder does - must not swallow the end,
                // or the feeder never reports 'ended' and TrackPlayer waits forever.
                try {
                    await this.systemDecoder?.flush();
                }
                catch (e) {
                    errorLog?.log(`#${this.streamId}.process: system decoder flush failed`, e);
                }

                void this.feederWorklet.end(this.mustAbort, rpcNoWait);
                return true;
            }

            if (this.systemDecoder) {
                const timing = this.createDecodeTiming(item.sourceOffsetMs);
                this.systemDecodeTimings.push(timing);
                const chunk = new EncodedAudioChunk({
                    data: item.data,
                    type: 'key',
                    duration: 20000, // 20ms
                    timestamp: this.chunkTimeOffset,
                });
                this.chunkTimeOffset += 20;
                this.systemDecoder.decode(chunk);
            }
            else if (this.decoder) {
                // typedViewSamples is a typed_memory_view to Decoder internal buffer - so you have to copy data
                const typedViewSamples = this.decoder.decode(item.data);
                if (typedViewSamples == null || typedViewSamples.length === 0) {
                    warnLog?.log(`#${this.streamId}.process: decoder returned empty result`);
                    this.rearmDecodeDemand();
                    return true;
                }

                const samplesBuffer = typedViewSamples.length == AUDIO.play.samplesPerWindow
                    ? this.bufferPool.get()
                    : this.largeBufferPool.get();
                const samples = new Float32Array(samplesBuffer, 0, typedViewSamples.length);
                samples.set(typedViewSamples);

                if (enableFrequentDebugLog)
                    debugLog?.log(
                        `#${this.streamId}.process: decoded ${item.data.byteLength} byte(s) into ` +
                        `${samples.byteLength} byte(s) / ${samples.length} samples`);
                const timing = this.createDecodeTiming(item.sourceOffsetMs);
                void this.feederWorklet.frame(
                    samples.buffer,
                    samples.byteOffset,
                    samples.length,
                    this.sourceRecordedAtMs,
                    timing.sourceOffsetMs,
                    rpcNoWait);
            }
        }
        catch (e) {
            errorLog?.log(`#${this.streamId}.process: error:`, e);
            this.rearmDecodeDemand();
        }
        // Keep running for reuse
        return true;
    }

    // Flow control is one token per side, and a pass that emits no frame loses both at once: the
    // feeder never asks again and the decoder never dequeues again. Re-arm to skip the bad packet.
    private rearmDecodeDemand(): void {
        this.frameRequested = true;
        this.flushDecodeDemand();
    }

    private onSystemDecoderError = (error: DOMException): void => {
        errorLog?.log(`onSystemDecoderError: `, error, this.streamId)
    }

    private onDecodedAudioChunk = (output: AudioData): void => {
        const timing = this.systemDecodeTimings.shift() ?? this.createDecodeTiming(0);
        const samplesBuffer = output.numberOfFrames == AUDIO.play.samplesPerWindow
            ? this.bufferPool.get()
            : this.largeBufferPool.get();
        const samples = new Float32Array(samplesBuffer, 0, output.numberOfFrames);
        output.copyTo(samples, { planeIndex: 0, format: 'f32-planar' })

        void this.feederWorklet.frame(
            samples.buffer,
            samples.byteOffset,
            samples.length,
            this.sourceRecordedAtMs,
            timing.sourceOffsetMs,
            rpcNoWait);
    }

    private flushDecodeDemand(): void {
        if (!this.frameRequested)
            return;

        const item = this.encodedFrames.shiftReady();
        if (item === undefined)
            return;

        this.frameRequested = false;
        this.processor.enqueue(item, false);
    }

    private createDecodeTiming(sourceOffsetMs: number): DecodeTiming {
        return {
            sourceOffsetMs,
        };
    }
}
