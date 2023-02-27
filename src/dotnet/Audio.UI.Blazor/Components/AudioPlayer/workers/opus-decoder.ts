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

/// #if MEM_LEAK_DETECTION
debugLog?.log(`MEM_LEAK_DETECTION == true`);
/// #endif

export class OpusDecoder implements AsyncDisposable {
    private readonly streamId: string;
    private readonly processor: AsyncProcessor<ArrayBufferView | 'end'>;
    private readonly feederWorklet: FeederAudioWorklet & Disposable;
    private decoder: Decoder;

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

        this.decoder?.delete();
        this.decoder = null;
        this.processor.clearQueue();
        this.end(false);
        await this.processor.whenRunning;
    }

    public decode(buffer: ArrayBuffer, offset: number, length: number,): void {
        warnLog?.assert(buffer.byteLength > 0, `#${this.streamId}.decode: got zero length buffer!`);
        const bufferView = new Uint8Array(buffer, offset, length);
        this.processor.enqueue(bufferView);
    }

    public stop(): void {
        this.processor.clearQueue();
    }

    public end(mustFailIfAlreadyStopped = true): void {
        this.processor.enqueue('end', mustFailIfAlreadyStopped);
    }

    private async process(item: ArrayBufferView | 'end'): Promise<void> {
        try {
            if (this.decoder == null) {
                debugLog?.log(`#${this.streamId}.process: decoder is disposed -> will shut down`);
                void this.processor.stop();
            }

            if (item === 'end') {
                debugLog?.log(`#${this.streamId}.process: got 'end'`);
                await this.feederWorklet.onEnd();
                return;
            }

            if (!this.decoder)
                return;

            const samples = this.decoder.decode(item);
            if (samples == null || samples.length === 0) {
                warnLog?.log(`#${this.streamId}.process: decoder returned empty result`);
                return;
            }

            debugLog?.log(
                `#${this.streamId}.process: decoded ${item.byteLength} byte(s) into ` +
                `${samples.byteLength} byte(s) / ${samples.length} samples`);
            void this.feederWorklet.onFrame(samples.buffer, samples.byteOffset, samples.length, rpcNoWait);
        }
        catch (e) {
            errorLog?.log(`#${this.streamId}.process: error:`, e);
        }
    }
}
