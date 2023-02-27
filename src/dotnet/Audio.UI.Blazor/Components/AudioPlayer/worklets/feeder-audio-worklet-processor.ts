import Denque from 'denque';
import { Log, LogLevel, LogScope } from 'logging';
import { timerQueue } from 'timerQueue';
import {
    BufferState,
    FeederAudioNode,
    FeederAudioWorklet,
    FeederState,
    PlaybackState,
} from './feeder-audio-worklet-contract';
import { rpcClientServer, rpcNoWait, RpcNoWait } from 'rpc';
import { OpusDecoderWorker } from '../workers/opus-decoder-worker-contract';
import { Disposable } from 'disposable';
import { ResolvedPromise } from 'promises';

const LogScope: LogScope = 'FeederProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SampleFrequency = 48000;
const SampleDuration = 1.0 / SampleFrequency;

/** Part of the feeder that lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor implements FeederAudioWorklet {
    private readonly chunks = new Denque<Float32Array | 'end'>();
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 240_000 samples at 48 kHz ~= 5_000 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private readonly lowBufferThreshold: number = 5.0;
    /** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
    private readonly enoughBufferThreshold: number = 10.0;
    /** How much seconds do we have in the buffer before we can start playing */
    private readonly enoughToPlayThreshold: number = 0.1;

    private id: string;
    private workletNode: FeederAudioNode & Disposable;
    private worker: OpusDecoderWorker & Disposable;
    // private workerPort: MessagePort;
    private chunkOffset = 0;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playingAt = 0;
    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'enough';

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.workletNode = rpcClientServer<FeederAudioNode>(`${LogScope}.server`, this.port, this);
    }

    private get bufferedDuration(): number {
        return this.bufferedSampleCount * SampleDuration;
    }

    private get bufferedSampleCount(): number {
        const { chunks, chunkOffset } = this;
        let result = -chunkOffset;
        const len = chunks.length;
        for (let i = 0; i < len; ++i) {
            const chunk = chunks.peekAt(i);
            result += chunk.length;
        }
        return result;
    }

    public async init(id: string, workerPort: MessagePort): Promise<void> {
        this.id = id;
        this.worker = rpcClientServer<OpusDecoderWorker>(`${LogScope}.worker`, workerPort, this);
        debugLog?.log(`#${this.id}.init`);
    }

    public async getState(): Promise<FeederState> {
        return {
            bufferedDuration: this.bufferedDuration,
            playingAt: this.playingAt,
            playbackState: this.playbackState,
            bufferState: this.bufferState,
        };
    }

    public frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        this.chunks.push(new Float32Array(buffer, offset, length));
        this.tryBeginPlaying();
        return ResolvedPromise.Void;
    }

    public pause(): Promise<void> {
        if (this.playbackState !== 'playing')
            return;

        debugLog?.log(`#${this.id}.pause`);
        this.playbackState = 'paused';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public resume(): Promise<void> {
        if (this.playbackState !== 'paused')
            return;

        debugLog?.log(`#${this.id}.resume`);
        this.playbackState = 'playing';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.end`);
        if (this.playbackState === 'playing') {
            if (mustAbort) {
                this.chunks.clear();
                this.chunkOffset = 0;
            }
            this.chunks.push('end');
        }
        else {
            this.playbackState = 'ended';
            this.stateHasChanged();
        }
        return ResolvedPromise.Void;
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        _parameters: { [name: string]: Float32Array; }
    ): boolean {
        timerQueue?.triggerExpired();
        if (outputs == null || outputs.length === 0 || outputs[0].length === 0)
            return true;

        const output = outputs[0];
        // We only support mono output at the moment
        const channel = output[0];
        warnLog?.assert(channel.length === 128, `#${this.id}.process: WebAudio's render quantum size must be 128`);

        if (this.playbackState !== 'playing') {
            // Write silence, because we aren't playing
            channel.fill(0);
            return this.playbackState !== 'ended';
        }

        // We're in 'playing' state anywhere below this point

        for (let channelOffset = 0; channelOffset < channel.length;) {
            const chunk = this.chunks.peekFront();
            if (chunk === undefined) {
                // Not enough data to continue playing => starving
                channel.fill(0, channelOffset);
                break;
            }

            if (chunk === 'end') {
                channel.fill(0, channelOffset);
                debugLog?.log(`#${this.id}.process: got 'end'`);
                this.playbackState = 'ended';
                this.stateHasChanged();
                return false;
            }

            const available = chunk.length - this.chunkOffset;
            const remaining = channel.length - channelOffset;
            const length = Math.min(available, remaining);

            const samples = chunk.subarray(this.chunkOffset, this.chunkOffset + length);
            channel.set(samples, channelOffset);
            this.chunkOffset += length;
            channelOffset += length;
            this.playingAt += length * SampleDuration;
            if (available < remaining) {
                this.chunkOffset = 0;
                this.chunks.shift();
            }
        }

        const bufferedDuration = this.bufferedDuration;
        const bufferState =
            (bufferedDuration < this.lowBufferThreshold)
            ? 'starving'
            : (bufferedDuration < this.enoughBufferThreshold ? 'low' : 'enough');
        if (this.bufferState != bufferState) {
            this.bufferState = bufferState;
            this.stateHasChanged();
        }
        return true;
    }

    private stateHasChanged() {
        void this.workletNode.stateChanged(this.playbackState, this.bufferState, rpcNoWait);
    }

    private tryBeginPlaying(): void {
        if (this.playbackState === 'playing' || this.bufferedDuration < this.enoughToPlayThreshold)
            return;

        debugLog?.log(`#${this.id}.tryBeginPlaying: starting playback`);
        this.playbackState = 'playing';
        this.playingAt = 0;
        this.stateHasChanged();
    }
}

// @ts-expect-error - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
