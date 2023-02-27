/// #if MEM_LEAK_DETECTION
import { Decoder } from '@actual-chat/codec/codec.debug';
/// #else
/// #code import { Decoder } from '@actual-chat/codec';
/// #endif
import 'logging-init';
import { rpcClient, rpcNoWait } from 'rpc';
import { FeederAudioWorklet } from '../worklets/feeder-audio-worklet-contract';
import { AsyncDisposable, Disposable } from 'disposable';
import { AsyncProcessor } from 'async-processor';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'OpusDecoder';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const enableFrequentDebugLog = false;

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

export class OpusDecoder implements AsyncDisposable {
    private readonly streamId: string;
    private readonly processor: AsyncProcessor<ArrayBufferView | 'end'>;
    private readonly feederWorklet: FeederAudioWorklet & Disposable;
    private decoder: Decoder;
    private mustAbort: boolean;
    private isEnded: boolean;

    public static async create(streamId: string, decoder: Decoder, workletPort: MessagePort): Promise<OpusDecoder> {
        return new OpusDecoder(streamId, decoder, workletPort);
    }

    /** accepts fully initialized decoder only, use the factory method `create` to construct an object */
    private constructor(streamId: string, decoder: Decoder, workletPort: MessagePort) {
        this.streamId = streamId;
        this.processor = new AsyncProcessor<ArrayBufferView | 'end'>('OpusDecoder', item => this.process(item));
        this.feederWorklet = rpcClient<FeederAudioWorklet>(`${LogScope}.feederWorklet`, workletPort);
        this.decoder = decoder;
    }

    public async disposeAsync(): Promise<void> {
        if (!this.decoder)
            return;

        this.end(true);
        await this.processor.whenRunning;
        this.decoder?.delete();
        this.decoder = null;
    }

    public decode(buffer: ArrayBuffer, offset: number, length: number,): void {
        warnLog?.assert(buffer.byteLength > 0, `#${this.streamId}.decode: got zero length buffer!`);
        const bufferView = new Uint8Array(buffer, offset, length);
        this.processor.enqueue(bufferView);
    }

    public end(mustAbort: boolean): void {
        this.mustAbort ||= mustAbort;
        if (this.mustAbort)
            this.processor.clearQueue();
        this.processor.enqueue('end');
    }

    private async process(item: ArrayBufferView | 'end'): Promise<void> {
        try {
            if (item === 'end') {
                this.isEnded = true;
                debugLog?.log(`#${this.streamId}.process: got 'end'`);
                await this.feederWorklet.end(this.mustAbort);
                return;
            }

            if (this.isEnded)
                return;

            const samples = this.decoder.decode(item);
            if (samples == null || samples.length === 0) {
                warnLog?.log(`#${this.streamId}.process: decoder returned empty result`);
                return;
            }

            if (enableFrequentDebugLog)
                debugLog?.log(
                    `#${this.streamId}.process: decoded ${item.byteLength} byte(s) into ` +
                    `${samples.byteLength} byte(s) / ${samples.length} samples`);
            void this.feederWorklet.frame(samples.buffer, samples.byteOffset, samples.length, rpcNoWait);
        }
        catch (e) {
            errorLog?.log(`#${this.streamId}.process: error:`, e);
        }
    }
}
