// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/require-await,@typescript-eslint/no-unsafe-assignment */
import { AUDIO, AppConstants, initAppConstants } from 'app-constants';
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
import { getLogs } from 'logging';
import { BufferHandler } from '../workers/opus-decoder-worker-contract';
import { AudioRingBuffer } from '../../AudioRecorder/audio-ring-buffer';
import { ServerClock } from 'clocks';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';

const { logScope, debugLog, warnLog } = getLogs('FeederProcessor');
// Buffered duration below the low-water mark signals the decoder to push frames;
// the decoder stops once buffered duration crosses the high-water mark.
// Hysteresis (low < high) bounds the demand toggle rate and shapes steady-state depth.
const FEEDER_LOW_WATER_FRAMES = 2;
const FEEDER_HIGH_WATER_FRAMES = 8;

interface DecodedChunk {
    samples: Float32Array;
    sourceRecordedAtMs: number;
    sourceOffsetMs: number;
    presentationLagMs: number;
}

/** Part of the feeder that lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor implements FeederAudioWorklet {
    public updateSharedSettings = (settings: SharedSettingsSnapshot, noWait?: RpcNoWait): Promise<void> =>
        sharedSettingsWorker.updateSharedSettings(settings, noWait);

    private readonly chunks = new Denque<DecodedChunk | 'end'>();
    private readonly buffer: AudioRingBuffer;
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 240_000 samples at 48 kHz ~= 5_000 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private id: string;
    private node: FeederAudioWorkletEventHandler & Disposable;
    private decoder: BufferHandler & Disposable;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playingAt = 0;
    private skipSamples = 0;
    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'ok';
    private lastReportedState: FeederState;
    private isEnding = false;
    private bufferSizeToStartPlayback!: number; // set in init() after initAppConstants
    private lastStarvingEventAt = 0;
    private demandSignaled = false;
    private presentationLagMs: number | null = null;
    private bufferHeadSourceOffsetMs: number | null = null;
    private bufferSourceRecordedAtMs: number | null = null;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.node = rpcClientServer<FeederAudioWorkletEventHandler>(`${logScope}.server`, this.port, this);
        this.buffer = new AudioRingBuffer(8192, 1);
    }

    private get bufferedDuration(): number {
        return this.bufferedSampleCount * AUDIO.play.sampleDuration;
    }

    private get bufferedSampleCount(): number {
        const { chunks, buffer } = this;
        let result = buffer.samplesAvailable;
        for (let i = 0; i <  chunks.length; ++i) {
            const chunk = chunks.peekAt(i);
            if (chunk !== 'end')
                result += chunk!.samples.length;
        }
        return result;
    }

    public async init(appConstants: AppConstants, sharedSettings: SharedSettingsSnapshot, id: string, workerPort: MessagePort): Promise<void> {
        await sharedSettingsWorker.updateSharedSettings(sharedSettings);
        initAppConstants(appConstants);
        this.bufferSizeToStartPlayback = this.feederLowWaterDuration;
        this.id = id;
        this.decoder = rpcClientServer<BufferHandler>(`${logScope}.worker`, workerPort, this);
        debugLog?.log(`#${this.id}.init`);
    }

    public frame(
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        sourceRecordedAtMs: number,
        sourceOffsetMs: number,
        presentationLagMs: number,
        noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended' || this.isEnding) {
            // Send buffer back
            void this.decoder.releaseBuffer(buffer, rpcNoWait);
            return ResolvedPromise.Void;
        }

        this.chunks.push({
            samples: new Float32Array(buffer, offset, length),
            sourceRecordedAtMs,
            sourceOffsetMs,
            presentationLagMs,
        });
        this.tryBeginPlaying();
        this.updateDemand();
        return ResolvedPromise.Void;
    }

    public pause(_noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'paused' || this.playbackState === 'ended')
            return ResolvedPromise.Void;

        debugLog?.log(`#${this.id}.pause`);
        this.playbackState = 'paused';
        this.stateHasChanged();
        return ResolvedPromise.Void;
    }

    public resume(preSkip: number): Promise<void> {
        this.playingAt = 0;
        this.skipSamples = preSkip;

        if (this.playbackState === 'playing')
            return ResolvedPromise.Void;

        debugLog?.log(`#${this.id}.resume`);
        this.playbackState = this.playbackState === 'ended'
            ? 'paused'
            : 'playing';
        this.stateHasChanged();
        this.updateDemand();
        return ResolvedPromise.Void;
    }

    public async end(mustAbort: boolean, _noWait?: RpcNoWait): Promise<void> {
        if (this.playbackState === 'ended') {
            if (!mustAbort)
                debugLog?.log(`#${this.id}.end, but playback is already ended`);
            return;
        }

        debugLog?.log(`#${this.id}.end, mustAbort:`, mustAbort);

        this.isEnding = true;
        this.playbackState = 'playing';
        if (mustAbort) {
            this.chunks.clear();
            this.playingAt = 0;
            this.buffer.reset();
        }
        this.chunks.push('end');
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        _parameters: Record<string, Float32Array>,
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
        // @ts-expect-error - accessible from the AudioWorkletGlobalScope
        const time = currentTime;
        if (this.buffer.samplesAvailable >= channel.length) {
            this.pullBufferedSamples(channel);
            this.updateDemand();
            this.stateHasChanged();
            return true;
        }

        while (this.buffer.samplesAvailable < channel.length) {
            const samplesAvailable = this.buffer.samplesAvailable;
            let chunk = this.chunks.shift();
            if (chunk === undefined) {
                this.updateDemand();
                // Not enough data to continue playing => starving
                channel.fill(0);
                if (samplesAvailable) {
                    const channelChunk = new Float32Array(channel.buffer, 0, samplesAvailable);
                    this.buffer.pull([channelChunk]);
                    this.playingAt += channelChunk.length * AUDIO.play.sampleDuration;
                }

                this.playbackState = 'starving';
                this.lastStarvingEventAt = time;
                this.stateHasChanged();
                return true;
            }
            else if (chunk === 'end') {
                channel.fill(0);
                debugLog?.log(`#${this.id}.process: got 'end'`);
                this.isEnding = false;
                this.playbackState = 'ended';
                this.playingAt = 0;
                this.buffer.reset();
                this.bufferHeadSourceOffsetMs = null;
                this.bufferSourceRecordedAtMs = null;
                while (chunk) {
                    chunk = this.chunks.shift();
                    if (chunk !== 'end' && chunk)
                        // @ts-expect-error TODO(AK): fix error
                        void this.decoder.releaseBuffer(chunk.samples.buffer, rpcNoWait);
                }
                this.chunks.clear();
                this.updateDemand();
                this.stateHasChanged();
                // Keep worklet up and running even in ended state for reuse
                return true;
            }
            if (this.buffer.samplesAvailable === 0) {
                this.bufferHeadSourceOffsetMs = chunk.sourceOffsetMs;
                this.bufferSourceRecordedAtMs = chunk.sourceRecordedAtMs;
            }
            this.buffer.push([chunk.samples]);
            // @ts-expect-error TODO(AK): fix error
            void this.decoder.releaseBuffer(chunk.samples.buffer, rpcNoWait);
            if (this.skipSamples) {
                const skipSamples = Math.min(this.skipSamples, this.buffer.samplesAvailable);
                this.buffer.pull([new Float32Array(skipSamples)]);
                this.advanceBufferHead(skipSamples);
                this.skipSamples -= skipSamples;
            }
        }
        this.pullBufferedSamples(channel);
        this.updateDemand();
        this.stateHasChanged();
        return true;
    }

    private pullBufferedSamples(channel: Float32Array): void {
        const outputSourceOffsetMs = this.bufferHeadSourceOffsetMs;
        const sourceRecordedAtMs = this.bufferSourceRecordedAtMs;
        this.buffer.pull([channel]);
        if (outputSourceOffsetMs !== null && sourceRecordedAtMs !== null)
            this.presentationLagMs = ServerClock.now() - (sourceRecordedAtMs + outputSourceOffsetMs);
        this.advanceBufferHead(channel.length);
        this.playingAt += channel.length * AUDIO.play.sampleDuration;
    }

    private advanceBufferHead(sampleCount: number): void {
        if (this.bufferHeadSourceOffsetMs !== null)
            this.bufferHeadSourceOffsetMs += sampleCount * AUDIO.play.sampleDuration * 1000;
        if (this.buffer.samplesAvailable === 0) {
            this.bufferHeadSourceOffsetMs = null;
            this.bufferSourceRecordedAtMs = null;
        }
    }

    private stateHasChanged() {
        const bufferedDuration = this.bufferedDuration;
        if (this.isEnding)
            this.bufferState = 'ok';
        else {
            this.bufferState = bufferedDuration < this.feederLowWaterDuration ? 'low' : 'ok';
        }

        const state: FeederState = {
            playbackState: this.playbackState,
            bufferState: this.bufferState,
            playingAt: this.playingAt,
            presentationLagMs: this.presentationLagMs,
            bufferedDuration: bufferedDuration,
        };
        const mustSkip =
            this.lastReportedState
            && state.playbackState === this.lastReportedState.playbackState
            && state.bufferState === this.lastReportedState.bufferState
            && Math.abs(state.playingAt - this.lastReportedState.playingAt) < AUDIO.play.stateUpdatePeriod;
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
        this.stateHasChanged();
    }

    private updateDemand(): void {
        if (!this.decoder)
            return;

        const stopped = this.playbackState === 'ended' || this.isEnding;
        if (stopped) {
            if (this.demandSignaled) {
                this.demandSignaled = false;
                void this.decoder.setDemand(false, this.feederTargetDelayMs, rpcNoWait);
            }
            return;
        }

        const buffered = this.bufferedDuration;
        if (this.demandSignaled) {
            if (buffered >= this.feederHighWaterDuration) {
                this.demandSignaled = false;
                void this.decoder.setDemand(false, this.feederTargetDelayMs, rpcNoWait);
            }
        }
        else if (buffered < this.feederLowWaterDuration) {
            this.demandSignaled = true;
            void this.decoder.setDemand(true, this.feederTargetDelayMs, rpcNoWait);
        }
    }

    private get feederLowWaterDuration(): number {
        return FEEDER_LOW_WATER_FRAMES / AUDIO.frameRate;
    }

    private get feederHighWaterDuration(): number {
        return FEEDER_HIGH_WATER_FRAMES / AUDIO.frameRate;
    }

    private get feederTargetDelayMs(): number {
        return this.feederHighWaterDuration * 1000;
    }
}

registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
