import Denque from 'denque';
import { Log, LogLevel, LogScope } from 'logging';
import { timerQueue } from 'timerQueue';
import { FeederAudioNode, FeederAudioWorklet, PlaybackState } from './feeder-audio-worklet-contract';
import { rpcClientServer, rpcNoWait, RpcNoWait, rpcServer } from 'rpc';
import { OpusDecoderWorker } from '../workers/opus-decoder-worker-contract';
import { Disposable } from 'disposable';
import { ResolvedPromise } from 'promises';

const LogScope: LogScope = 'FeederProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const SAMPLE_RATE = 48000;

/** Part of the feeder that lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor implements FeederAudioWorklet {
    private readonly chunks = new Denque<Float32Array | 'end'>();
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 240_000 samples at 48 kHz ~= 5_000 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private readonly samplesLowThreshold: number = 480_000;
    /**
     * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
     * should be in sync with audio-feeder bufferSize
     */
    private readonly enoughToStartPlaying: number = 0.1;
    /** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
    private readonly tooMuchBuffered: number = 15.0;

    private workletNode: FeederAudioNode & Disposable;
    private worker: OpusDecoderWorker & Disposable;
    // private workerPort: MessagePort;
    private chunkOffset = 0;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playbackTime = 0;
    private isPlaying = false;
    private isPaused = false;
    private isStarving = false;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        debugLog?.log('ctor');
        this.workletNode = rpcClientServer<FeederAudioNode>(`${LogScope}.server`, this.port, this);
    }

    /** Count how many samples are queued up */
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

    public async init(workerPort: MessagePort): Promise<void> {
        this.worker = rpcClientServer<OpusDecoderWorker>(`${LogScope}.worker`, workerPort, this);
    }

    public getState(): Promise<PlaybackState> {
        const playbackState: PlaybackState = {
            bufferedTime: this.bufferedSampleCount / SAMPLE_RATE,
            playbackTime: this.playbackTime,
        };
        return Promise.resolve(playbackState);
    }

    public stop(): Promise<void> {
        const { isPlaying: wasPlaying } = this;
        this.reset();

        if (wasPlaying) {
            debugLog?.log(`stop`);
            void this.workletNode.onStateChanged('stopped', rpcNoWait);
        }
        void this.workletNode.onStateChanged('ended', rpcNoWait);
        return ResolvedPromise.Void;
    }

    public pause(): Promise<void> {
        if (this.isPaused) {
            debugLog?.log(`onPauseMessage: already in pause state:`, this.isPaused);
            return;
        }
        this.isPaused = true;
        void this.workletNode.onStateChanged('paused', rpcNoWait);
        return ResolvedPromise.Void;
    }

    public resume(): Promise<void> {
        if (!this.isPaused) {
            debugLog?.log(`onPauseMessage: already in resumed state:`, this.isPaused);
            return;
        }
        this.isPaused = false;
        void this.workletNode.onStateChanged('resumed', rpcNoWait);
        return ResolvedPromise.Void;
    }

    public onEnd(noWait?: RpcNoWait): Promise<void> {
        this.chunks.push('end');
        // if we don't start to play and the 'end' is already here
        // for example if play threshold > number of frames before the end
        if (!this.isPlaying) {
            this.reset();
            void this.workletNode.onStateChanged('ended', rpcNoWait);
        }
        return ResolvedPromise.Void;
    }

    /** Decoded samples from the decoder worker */
    public onFrame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void> {
        this.chunks.push(new Float32Array(buffer, offset, length));
        this.startPlaybackIfEnoughBuffered();
        return ResolvedPromise.Void;
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        _parameters: { [name: string]: Float32Array; }
    ): boolean {
        timerQueue?.triggerExpired();
        const { chunks, isPlaying, isPaused, samplesLowThreshold, tooMuchBuffered } = this;
        let { chunkOffset } = this;
        if (outputs == null || outputs.length === 0 || outputs[0].length === 0) {
            return true;
        }

        const output = outputs[0];
        // we only support mono output at the moment
        const channel = output[0];
        warnLog?.assert(channel.length === 128, `process: WebAudio's render quantum size must be 128`);

        if (!isPlaying || isPaused) {
            // write silence, because we don't playing
            channel.fill(0);
            return true;
        }

        for (let offset = 0; offset < channel.length;) {
            const chunk = chunks.peekFront();
            if (chunk !== undefined) {
                if (chunk === 'end') {
                    channel.fill(0, offset);
                    debugLog?.log(`process: got 'end'`);
                    void this.stop();
                    break;
                }
                else {
                    const chunkAvailable = chunk.length - chunkOffset;
                    const remaining = channel.length - offset;

                    if (chunkAvailable >= remaining) {
                        const remainingSamples = chunk.subarray(chunkOffset, chunkOffset + remaining);
                        channel.set(remainingSamples, offset);
                        chunkOffset += remaining;
                        offset += remaining;
                        this.playbackTime += remaining / SAMPLE_RATE;
                    }
                    else {
                        const remainingSamples = chunk.subarray(chunkOffset);
                        channel.set(remainingSamples, offset);
                        offset += remainingSamples.length;
                        this.playbackTime += remainingSamples.length / SAMPLE_RATE;

                        chunkOffset = 0;
                        chunks.shift();
                    }
                }
            }
            // we don't have enough data to continue playing => starving
            else {
                channel.fill(0, offset);
                if (!this.isStarving) {
                    this.isStarving = true;
                    void this.workletNode.onStateChanged('starving', rpcNoWait);
                }

                break;
            }
        }
        this.chunkOffset = chunkOffset;
        const sampleCount = this.bufferedSampleCount;
        const bufferedDuration = sampleCount / SAMPLE_RATE;
        if (this.isPlaying && !this.isStarving) {
            if (sampleCount <= samplesLowThreshold) {
                void this.workletNode.onStateChanged('playingWithLowBuffer', rpcNoWait);
            }
            if (bufferedDuration > tooMuchBuffered) {
                void this.workletNode.onStateChanged('playingWithTooMuchBuffer', rpcNoWait);
            }
        } else if (this.isStarving) {
            if (sampleCount > samplesLowThreshold) {
                this.isStarving = false;

                if (this.isPlaying) {
                    void this.workletNode.onStateChanged('playing', rpcNoWait);
                }
            }
        }
        return true;
    }

    private reset(): void {
        debugLog?.log(`reset`);
        this.isPlaying = false;
        this.isPaused = false;
        this.isStarving = false;
        this.chunks.clear();
        this.chunkOffset = 0;
        // we don't set playbackTime = 0, because we want to see it in getState() after a stop.
    }

    private startPlaybackIfEnoughBuffered(): void {
        if (!this.isPlaying) {
            const bufferedDuration = this.bufferedSampleCount / SAMPLE_RATE;
            if (bufferedDuration >= this.enoughToStartPlaying) {
                this.isPlaying = true;
                this.playbackTime = 0;
                void this.workletNode.onStateChanged('playing', rpcNoWait);
            }
        }
    }
}

// @ts-expect-error - registerProcessor exists
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
