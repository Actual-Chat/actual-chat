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
import { ResolvedPromise } from 'actuallab-core';
import { getLogs } from 'logging';
import { BufferHandler } from '../workers/opus-decoder-worker-contract';
import { AudioRingBuffer } from '../../AudioRecorder/audio-ring-buffer';
import { ServerClock } from 'clocks';
import { type SharedSettingsSnapshot } from 'shared-settings';
import { sharedSettingsWorker } from 'shared-settings-worker';

const { logScope, debugLog, warnLog } = getLogs('FeederProcessor');

interface DecodedChunk {
    samples: Float32Array;
    sourceRecordedAtMs: number;
    sourceOffsetMs: number;
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
    private frameRequestPending = false;
    private presentationLagMs: number | null = null;
    private recordedStartMs: number | null = null;
    private nextExpectedOffsetMs: number | null = null;
    private _targetBufferedDuration?: number;

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
        this.bufferSizeToStartPlayback = this.targetBufferedDuration;
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
        noWait?: RpcNoWait): Promise<void> {
        // debugLog?.log(`#${this.id} -> frame()`);
        this.frameRequestPending = false;
        if (this.playbackState === 'ended' || this.isEnding) {
            // Send buffer back
            void this.decoder.releaseBuffer(buffer, rpcNoWait);
            return ResolvedPromise.Void;
        }

        this.chunks.push({
            samples: new Float32Array(buffer, offset, length),
            sourceRecordedAtMs,
            sourceOffsetMs,
        });
        this.tryBeginPlaying();
        this.requestFrameIfLow();
        // debugLog?.log(`#${this.id} <- frame()`);
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
        this.recordedStartMs = null;
        this.nextExpectedOffsetMs = null;

        if (this.playbackState === 'playing')
            return ResolvedPromise.Void;

        debugLog?.log(`#${this.id}.resume`);
        this.playbackState = this.playbackState === 'ended'
            ? 'paused'
            : 'playing';
        this.stateHasChanged();
        this.requestFrameIfLow();
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
            this.recordedStartMs = null;
            this.nextExpectedOffsetMs = null;
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
            this.requestFrameIfLow();
            this.stateHasChanged();
            return true;
        }

        while (this.buffer.samplesAvailable < channel.length) {
            const samplesAvailable = this.buffer.samplesAvailable;
            let chunk = this.chunks.shift();
            if (chunk === undefined) {
                this.requestFrameIfLow();
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
                this.recordedStartMs = null;
                this.nextExpectedOffsetMs = null;
                while (chunk) {
                    chunk = this.chunks.shift();
                    if (chunk !== 'end' && chunk)
                        // @ts-expect-error TODO(AK): fix error
                        void this.decoder.releaseBuffer(chunk.samples.buffer, rpcNoWait);
                }
                this.chunks.clear();
                this.stateHasChanged();
                // Keep worklet up and running even in ended state for reuse
                return true;
            }
            this.trackChunkOffset(chunk);
            this.buffer.push([chunk.samples]);
            // @ts-expect-error TODO(AK): fix error
            void this.decoder.releaseBuffer(chunk.samples.buffer, rpcNoWait);
            if (this.skipSamples) {
                const skipSamples = Math.min(this.skipSamples, this.buffer.samplesAvailable);
                this.buffer.pull([new Float32Array(skipSamples)]);
                // Discarded preSkip samples still advance the source position.
                this.playingAt += skipSamples * AUDIO.play.sampleDuration;
                this.skipSamples -= skipSamples;
            }
        }
        this.pullBufferedSamples(channel);
        this.requestFrameIfLow();
        this.stateHasChanged();
        return true;
    }

    private pullBufferedSamples(channel: Float32Array): void {
        this.buffer.pull([channel]);
        this.playingAt += channel.length * AUDIO.play.sampleDuration;
        this.updatePresentationLag();
    }

    // playingAt tracks the played source position. Hard-skip / speed-up drop
    // encoded frames upstream, so the next chunk's sourceOffsetMs jumps forward;
    // we add that gap to playingAt so it keeps tracking the true source position
    // (preSkip-discarded samples are bumped separately, at the pull site).
    private trackChunkOffset(chunk: DecodedChunk): void {
        if (this.recordedStartMs === null)
            this.recordedStartMs = chunk.sourceRecordedAtMs + chunk.sourceOffsetMs - this.playingAt * 1000;
        else if (this.nextExpectedOffsetMs !== null && chunk.sourceOffsetMs > this.nextExpectedOffsetMs + 1)
            this.playingAt += (chunk.sourceOffsetMs - this.nextExpectedOffsetMs) / 1000;
        this.nextExpectedOffsetMs = chunk.sourceOffsetMs + chunk.samples.length * AUDIO.play.sampleDuration * 1000;
    }

    private updatePresentationLag(): void {
        if (this.recordedStartMs === null)
            return;
        this.presentationLagMs = ServerClock.now() - (this.recordedStartMs + this.playingAt * 1000);
    }

    private stateHasChanged() {
        const bufferedDuration = this.bufferedDuration;
        if (this.isEnding)
            this.bufferState = 'ok';
        else {
            this.bufferState = bufferedDuration < this.targetBufferedDuration ? 'low' : 'ok';
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

    private requestFrameIfLow(): void {
        if (!this.decoder || this.frameRequestPending || this.playbackState === 'ended' || this.isEnding)
            return;
        if (this.bufferedDuration >= this.targetBufferedDuration)
            return;

        this.frameRequestPending = true;
        void this.decoder.requestFrame(rpcNoWait);
    }

    private get targetBufferedDuration(): number {
        return this._targetBufferedDuration ??=
            (AUDIO.play.decodedBufferSizeMs / (1000 / AUDIO.frameRate)) / AUDIO.frameRate;
    }
}

registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
