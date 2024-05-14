import Denque from 'denque';
import { timerQueue } from 'timerQueue';
import {
    BufferState,
    FeederAudioWorkletEventHandler,
    FeederAudioWorklet,
    FeederState,
    PlaybackState,
} from './feeder-audio-worklet-contract';
import { rpcClientServer, rpcNoWait, RpcNoWait } from 'rpc';
import { Disposable } from 'disposable';
import { ResolvedPromise } from 'promises';
import { Log } from 'logging';
import { BufferHandler } from '../workers/opus-decoder-worker-contract';
import { SAMPLE_RATE } from '../constants';

const { logScope, debugLog, warnLog } = Log.get('FeederProcessor');

const SampleDuration = 1.0 / SAMPLE_RATE;
const PlayableBufferSize = 0.15; // In seconds, 1s = 5 * 20ms OPUS frames
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
    private decoder: BufferHandler & Disposable;
    private chunkOffset = 0;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playingAt = 0;
    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'ok';
    private lastReportedState: FeederState = null;
    private isEnding = false;
    private bufferSizeToStartPlayback = PlayableBufferSize;
    private lastStarvingEventAt: number = 0;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.node = rpcClientServer<FeederAudioWorkletEventHandler>(`${logScope}.server`, this.port, this);
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
        this.decoder = rpcClientServer<BufferHandler>(`${logScope}.worker`, workerPort, this);
        debugLog?.log(`#${this.id}.init`);
    }

    public frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void> {
        // debugLog?.log(`#${this.id} -> frame()`);
        if (this.playbackState === 'ended' || this.isEnding)
            return;

        this.chunks.push(new Float32Array(buffer, offset, length));
        this.tryBeginPlaying();
        // debugLog?.log(`#${this.id} <- frame()`);
        return ResolvedPromise.Void;
    }

    public pause(_noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'paused' || this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.pause`);
        this.playbackState = 'paused';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public resume(): Promise<void> {
        if (this.playbackState === 'playing')
            return;

        debugLog?.log(`#${this.id}.resume`);
        this.playbackState = this.playbackState === 'ended'
            ? 'paused'
            : 'playing';
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
            this.playingAt = 0;
        }
        this.chunks.push('end');
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        _parameters: { [name: string]: Float32Array; },
    ): boolean {
        timerQueue?.triggerExpired();
        if (outputs == null || outputs.length === 0 || outputs[0].length === 0)
            return true;

        const output = outputs[0];
        // We only support mono output at the moment
        const channel = output[0];
        warnLog?.assert(channel.length === 128, `#${this.id}.process: WebAudio's render quantum size must be 128`);

        if (this.playbackState !== 'playing') {
            // Write silence, because we aren't playing (even when starving)
            channel.fill(0);
            // Keep worklet up and running even in ended state for reuse
            return true;
        }

        // We're in 'playing' state anywhere below this point
        // @ts-ignore - accessible from the AudioWorkletGlobalScope
        const time = currentTime;
        for (let channelOffset = 0; channelOffset < channel.length;) {
            let chunk = this.chunks.peekFront();
            if (chunk === undefined) {
                // Not enough data to continue playing => starving
                channel.fill(0, channelOffset);
                this.playbackState = 'starving';

                if (time - this.lastStarvingEventAt > 1000)
                    // increase buffer size to prevent starving if previous event has happened earlier than 1s before
                    this.bufferSizeToStartPlayback += PlayableBufferSize;
                this.lastStarvingEventAt = time;
                break;
            }

            // decrease buffer size when there were no starving events during last 5s
            if (time - this.lastStarvingEventAt > 5000)
                this.bufferSizeToStartPlayback = Math.max(
                    this.bufferSizeToStartPlayback - PlayableBufferSize,
                    PlayableBufferSize);

            if (chunk === 'end') {
                channel.fill(0, channelOffset);
                debugLog?.log(`#${this.id}.process: got 'end'`);
                this.isEnding = false;
                this.playbackState = 'ended';
                this.chunkOffset = 0;
                this.playingAt = 0;
                while (chunk) {
                    chunk = this.chunks.shift();
                    if (chunk !== 'end' && chunk)
                        void this.decoder.releaseBuffer(chunk.buffer, rpcNoWait);
                }
                this.chunks.clear();
                this.stateHasChanged();
                // Keep worklet up and running even in ended state for reuse
                return true;
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
                void this.decoder.releaseBuffer(chunk.buffer, rpcNoWait);
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
        };
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
        if (this.playbackState === 'playing' || this.bufferedDuration < this.bufferSizeToStartPlayback)
            return;

        debugLog?.log(`#${this.id}.tryBeginPlaying: starting playback`);
        this.isEnding = false;
        this.playbackState = 'playing';
        this.chunkOffset = 0;
        this.playingAt = 0;
        this.stateHasChanged();
    }
}

registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
