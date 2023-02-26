import Denque from 'denque';
/// #if MEM_LEAK_DETECTION
import { Decoder } from '@actual-chat/codec/codec.debug';
/// #else
/// #code import { Decoder } from '@actual-chat/codec';
/// #endif
import { Log, LogLevel, LogScope } from 'logging';
import 'logging-init';
import { rpcClient } from 'rpc';
import { FeederAudioWorklet } from '../worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';

const LogScope: LogScope = 'OpusDecoder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

export class OpusDecoder implements Disposable {
    private readonly queue = new Denque<ArrayBufferView | 'end'>();
    private readonly feederWorklet: FeederAudioWorklet & Disposable;

    private decoder: Decoder;
    private state: 'waiting' | 'decoding' = 'waiting';

    public static async create(decoder: Decoder, workletPort: MessagePort): Promise<OpusDecoder> {
        return new OpusDecoder(decoder, workletPort);
    }

    /** accepts fully initialized decoder only, use the factory method `create` to construct an object */
    private constructor(decoder: Decoder, workletPort: MessagePort) {
        this.decoder = decoder;
        this.feederWorklet = rpcClient<FeederAudioWorklet>(`${LogScope}.feederWorklet`, workletPort);
        this.state = 'waiting';
    }

    public decode(buffer: ArrayBuffer, offset: number, length: number,): void {
        debugLog?.log(`pushData: data size: ${buffer.byteLength} byte(s)`);
        warnLog?.assert(buffer.byteLength > 0, `pushData: got zero length data message!`);
        const { queue } = this;
        const bufferView = new Uint8Array(buffer, offset, length);
        queue.push(bufferView);
        this.processQueue();
    }

    public stop(): void {
        debugLog?.log(`stop`);
        const { queue } = this;
        queue.clear();
    }

    public end(): void {
        const { queue } = this;
        queue.push('end');
        this.processQueue();
    }

    private processQueue(): void {
        const { queue, feederWorklet } = this;

        if (this.state === 'decoding')
            return;

        try {
            this.state = 'decoding';
            while(true) {
                if (queue.isEmpty())
                    return;

                const item = queue.shift();
                if (item === 'end') {
                    debugLog?.log(`processQueue: end is reached, sending end to worklet and stopping queue processing`);
                    // tell the worklet, that we are at the end of playing
                    void feederWorklet.onEnd();
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

                void feederWorklet.onSamples(samples.buffer, samples.byteOffset, samples.length);
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

    public dispose(): void {
        this.decoder?.delete();
        this.decoder = null;
    }
}
