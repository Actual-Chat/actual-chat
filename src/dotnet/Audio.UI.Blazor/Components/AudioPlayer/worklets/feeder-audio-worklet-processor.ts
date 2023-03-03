import Denque from 'denque';
import { timerQueue } from 'timerQueue';
import {
    BufferState,
    FeederAudioWorkletEventHandler,
    FeederAudioWorklet,
    FeederState,
    PlaybackState,
} from './feeder-audio-worklet-contract';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import { Disposable } from 'disposable';
import { PromiseSource, ResolvedPromise } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'FeederProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const SampleFrequency = 48000;
const SampleDuration = 1.0 / SampleFrequency;
const PlayableBufferSize = 0.05; // In seconds
const OkBufferSize = 10.0; // In seconds
const StateUpdatePeriod = 0.2;

/** Part of the feeder that lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor implements FeederAudioWorklet {
    private readonly chunks = new Denque<Float32Array | 'end'>();
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 240_000 samples at 48 kHz ~= 5_000 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private id: string;
    private node: FeederAudioWorkletEventHandler & Disposable;
    private worker: Disposable;
    private chunkOffset = 0;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playingAt = 0;
    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'ok';
    private lastReportedState: FeederState = null;
    private isEnding = false;
    private whenEnded = new PromiseSource<void>();

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.node = rpcClientServer<FeederAudioWorkletEventHandler>(`${LogScope}.server`, this.port, this);
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
        this.worker = rpcServer(`${LogScope}.worker`, workerPort, this);
        debugLog?.log(`#${this.id}.init`);
    }

    public frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended' || this.isEnding)
            return;

        this.chunks.push(new Float32Array(buffer, offset, length));
        this.tryBeginPlaying();
        return ResolvedPromise.Void;
    }

    public pause(_noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState !== 'playing')
            return;

        debugLog?.log(`#${this.id}.pause`);
        this.playbackState = 'paused';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public resume(_noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState !== 'paused')
            return;

        debugLog?.log(`#${this.id}.resume`);
        this.playbackState = 'playing';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public async end(mustAbort: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended') {
            warnLog?.log(`#${this.id}.end, but playback is already ended`);
            return;
        }

        debugLog?.log(`#${this.id}.end, mustAbort:`, mustAbort);

        this.isEnding = true;
        this.playbackState = 'playing';
        if (mustAbort) {
            this.chunks.clear();
            this.chunkOffset = 0;
        }
        this.chunks.push('end');
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
                this.whenEnded.resolve(undefined);
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

        this.stateHasChanged();
        return true;
    }

    private stateHasChanged() {
        const bufferedDuration = this.bufferedDuration;
        if (this.isEnding)
            this.bufferState = 'ok';
        else {
            this.bufferState = bufferedDuration < OkBufferSize ? 'low' : 'ok';
        }

        const state: FeederState = {
            playbackState: this.playbackState,
            bufferState: this.bufferState,
            playingAt: this.playingAt,
            bufferedDuration: bufferedDuration,
        }
        const mustSkip =
            this.lastReportedState
            && state.playbackState === this.lastReportedState.playbackState
            && state.bufferState === this.lastReportedState.bufferState
            && Math.abs(state.playingAt - this.lastReportedState.playingAt) < StateUpdatePeriod;
        if (mustSkip)
            return;

        this.lastReportedState = state;
        void this.node.onStateChanged(state, rpcNoWait);
    }

    private tryBeginPlaying(): void {
        if (this.playbackState === 'playing' || this.bufferedDuration < PlayableBufferSize)
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
