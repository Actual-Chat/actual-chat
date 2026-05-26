// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-unsafe-enum-comparison,@typescript-eslint/no-explicit-any */
import { AUDIO } from 'app-constants';
import { clamp, lerp, RunningUnitMedian, RunningEMA, approximateGain } from 'math';
import { ResolvedPromise } from 'actuallab-core';
import * as ort from 'onnxruntime-web';
import ortMjs from './ort-wasm-simd.mjs'
import ortWasm from './ort-wasm-simd.wasm'
// import ortWasm from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm'
// import ortMjs from 'onnxruntime-web/dist/ort-wasm-simd-threaded.mjs'
import { WebRtcVad } from '@actual-chat/webrtc-vad';
import { VoiceActivityChange, VoiceActivityDetector, NO_VOICE_ACTIVITY } from './audio-vad-contract';
// import { getLogs } from 'logging';
import { Versioning } from 'versioning';

// const { debugLog } = getLogs('AudioVadWorker');

export abstract class VoiceActivityDetectorBase implements VoiceActivityDetector {
    protected readonly probEMA = new RunningEMA(0.5, this.isNeural ? 2 : 5); // 32ms*2 ~ 64ms for Silero (fast onset); 5 for WebRTC
    protected readonly longProbEMA = new RunningEMA(0.5, 64); // 32ms*64 ~ 2s
    protected readonly probMedian = new RunningUnitMedian();
    protected readonly minSpeechSamples: number;
    protected readonly maxSpeechSamples: number;
    protected readonly maxPauseCancelSamples: number;
    // private readonly results =  new Array<number>();

    protected sampleCount = 0;
    protected pauseOffset: number | null = null;
    protected pauseCancelSamples = 0;
    protected lastConversationSignalAtSample: number | null = null;
    protected whenTalkingProbMedian: RunningUnitMedian | null = null;
    protected maxPauseSamples: number;

    protected constructor(
        protected isNeural: boolean,
        protected sampleRate: number,
        public lastActivityEvent: VoiceActivityChange = NO_VOICE_ACTIVITY,
    ) {
        this.minSpeechSamples = sampleRate * AUDIO.vad.minSpeech;
        this.maxSpeechSamples = sampleRate * AUDIO.vad.maxSpeech;
        this.maxPauseSamples = sampleRate * AUDIO.vad.maxPause;
        this.maxPauseCancelSamples = sampleRate * AUDIO.vad.minSpeechToCancelPause;
    }

    public abstract init(): Promise<void>;

    public reset(): void {
        this.lastActivityEvent = NO_VOICE_ACTIVITY;
        this.sampleCount = 0;
        this.pauseOffset = null;
        this.pauseCancelSamples = 0;
        this.lastConversationSignalAtSample = null;
        if (this.resetInternal)
            this.resetInternal();
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | number> {
        // Support batched input: length must be N * windowSamples
        const windowSamples = this.isNeural ? AUDIO.vad.neuralFrameSamples : AUDIO.vad.webrtcFrameSamples;
        if (monoPcm.length % windowSamples !== 0)
            throw new Error(`appendChunk() accepts ${windowSamples}*N sample audio windows only, but found ${monoPcm.length}.`);

        let currentEvent = this.lastActivityEvent;
        const startOffset = this.sampleCount - monoPcm.length;
        this.sampleCount += monoPcm.length;

        const gain = approximateGain(monoPcm);
        if (gain < AUDIO.rec.minRecordingGain && currentEvent.kind === 'end') {
            // do not try to check VAD at low gain input
            return gain;
        }

        // Query probabilities in one shot (for neural) or per-frame (for WebRTC)
        const probs = await this.appendChunkInternal(monoPcm);
        // If not initialized yet
        if (probs === null)
            return gain;

        const frames = monoPcm.length / windowSamples;
        for (let i = 0; i < frames; i++) {
            const frameOffset = startOffset + i * windowSamples;
            const prob = probs[i];
            //debugLog?.log('appendChunk:', prob, gain);
            // this.results.push(prob);
            this.probEMA.appendSample(prob);
            this.longProbEMA.appendSample(prob);
            const probEma = this.probEMA.value;
            const longProbEma = this.longProbEMA.value;
            const probMedian = this.probMedian.value;
            const speechProbTrigger = 0.67 * probMedian;
            const pauseProbTrigger = 0.15 * probMedian;

            if (currentEvent.kind === 'end'
                && probEma >= longProbEma
                && (probEma >= speechProbTrigger)
            ) {
                // speech start detected
                const offset = Math.max(0, frameOffset - windowSamples);
                const duration = offset - currentEvent.offset;
                currentEvent = { kind: 'start', offset, speechProb: probEma, duration };
                this.whenTalkingProbMedian = new RunningUnitMedian();
                this.maxPauseSamples = this.sampleRate * AUDIO.vad.maxPause;
            }
            else if (currentEvent.kind === 'start' && probEma < longProbEma && probEma < pauseProbTrigger) {
                // pause start detected
                this.pauseCancelSamples = 0;
                this.pauseOffset ??= frameOffset;
                this.whenTalkingProbMedian = null;
            }

            if (currentEvent.kind === 'start') {
                const currentSpeechSamples = frameOffset - currentEvent.offset;

                if (this.pauseOffset !== null) {
                    // we detected pause earlier - should we "materialize" it?
                    if (probEma >= speechProbTrigger) {
                        // and it's speech now
                        this.pauseCancelSamples += windowSamples;
                        if (this.pauseCancelSamples >= this.maxPauseCancelSamples) {
                            // which continues for
                            this.pauseOffset = null;
                            this.pauseCancelSamples = 0;
                        }
                    } else if (probEma < pauseProbTrigger) {
                        // it's still a pause
                        const currentSilenceSamples = frameOffset - (this.pauseOffset ?? 0);
                        if (currentSilenceSamples > this.maxPauseSamples && currentSpeechSamples > this.minSpeechSamples) {
                            // "materializing" the pause
                            const offset = this.pauseOffset + windowSamples;
                            const duration = offset - currentEvent.offset;
                            currentEvent = { kind: 'end', offset, speechProb: probEma, duration };
                            this.pauseOffset = null;
                        }
                    }
                }
                else if (this.whenTalkingProbMedian !== null) {
                    // adjust speech boundary triggers if current period was speech with high probabilities
                    const offset = frameOffset + windowSamples;
                    const duration = offset - currentEvent.offset;
                    const durationS = duration / this.sampleRate;
                    const speechRatio = this.whenTalkingProbMedian.sampleCount / duration;
                    if (speechRatio > 0.5 && durationS > 2)
                        this.probMedian.appendSample(probEma);
                }

                if (currentEvent.kind === 'start' && currentSpeechSamples > this.maxSpeechSamples) {
                    // break long speech regardless of speech probability
                    const offset = this.pauseOffset ?? frameOffset;
                    const duration = offset - currentEvent.offset;
                    currentEvent = { kind: 'end', offset: frameOffset, speechProb: probEma, duration };
                    this.pauseOffset = null;
                }

                if (this.whenTalkingProbMedian !== null && prob > 0.25)
                    this.whenTalkingProbMedian.appendSample(prob);

                // adjust max pause for long speech - break more aggressively, but keep longer pauses for monologue
                const isConversation = this.lastConversationSignalAtSample !== null
                    && (this.sampleCount - this.lastConversationSignalAtSample) <= this.sampleRate * AUDIO.vad.convDuration;
                const maxPause = isConversation ? AUDIO.vad.maxConvPause : AUDIO.vad.maxPause;
                const maxPauseVariesFromSamples = this.sampleRate * AUDIO.vad.pauseVariesFrom;
                let maxPauseAlpha =
                    (currentSpeechSamples - maxPauseVariesFromSamples) /
                    (this.maxSpeechSamples - maxPauseVariesFromSamples); // Always > 0
                maxPauseAlpha = clamp(maxPauseAlpha, 0, 1);
                maxPauseAlpha = Math.pow(maxPauseAlpha, AUDIO.vad.pauseVaryPower);
                const silenceThreshold = lerp(maxPause, AUDIO.vad.minPause, maxPauseAlpha);
                this.maxPauseSamples = Math.floor(this.sampleRate * silenceThreshold);
            }

            if (this.lastActivityEvent !== currentEvent && this.lastActivityEvent.kind !== currentEvent.kind) {
                this.lastActivityEvent = currentEvent;
                return adjustChangeEventsToSeconds(currentEvent, this.sampleRate);
            }
        }

        return gain;
    }

    public conversationSignal(): void {
        this.lastConversationSignalAtSample = this.sampleCount;
    }

    protected resetInternal?(): void;
    protected abstract appendChunkInternal(monoPcm: Float32Array): Promise<number[] | null>;
}

// WebRtcVoiceActivityDetector

enum VadActivity {
    Silence = 0,
    Voice = 1,
    Error = -1,
}

export class WebRtcVoiceActivityDetector extends VoiceActivityDetectorBase {
    private lastSkippedAt = 0;

    constructor(private baseVad: WebRtcVad) {
        super(false, AUDIO.rec.sampleRate);
    }

    public override init(): Promise<void> {
        return ResolvedPromise.Void;
    }

    protected override appendChunkInternal(monoPcm: Float32Array): Promise<number[] | null> {
        if (monoPcm.length % AUDIO.vad.webrtcFrameSamples !== 0)
            throw new Error(`appendChunk() accepts ${AUDIO.vad.webrtcFrameSamples}*N sample audio windows only.`);

        const frames = monoPcm.length / AUDIO.vad.webrtcFrameSamples;
        const probs: number[] = [];
        for (let i = 0; i < frames; i++) {
            const frame = monoPcm.subarray(i * AUDIO.vad.webrtcFrameSamples, (i + 1) * AUDIO.vad.webrtcFrameSamples);
            // Emscripten interop requires Uint8Array or ArrayBuffer, so we need to pass Float32Array as Uint8Array
            // @ts-expect-error TODO(AK): fix the error
            const activity = this.baseVad.detect(new Uint8Array(frame.buffer, frame.byteOffset, frame.length * 4));
            const now = Date.now();
            if (activity > 0 && (this.sampleCount / AUDIO.rec.samplesPerMs < AUDIO.vad.skipFirstRecordingMs || now - this.lastSkippedAt < 5)) {
                this.lastSkippedAt = now;
                return Promise.resolve(null); // Skip triggering on microphone noise at the beginning of the first microphone stream
            }
            if (activity == VadActivity.Error)
                throw new Error(`Error calling WebRtc VAD`);
            // Map 1|0 to ~[0,0.8]
            probs.push(0.8 * activity);
        }
        return Promise.resolve(probs);
    }

    protected override resetInternal() {
        this.baseVad.reset();
    }
}

// NNVoiceActivityDetector

export class NeuralVoiceActivityDetector extends VoiceActivityDetectorBase {
    private readonly modelUri: URL;

    private readonly buffer: Float32Array; // legacy buffer; not used with batched model

    private session: ort.InferenceSession | null = null;
    private state: ort.Tensor;
    private context: ort.Tensor;

    constructor(modelUri: URL, lastActivityEvent: VoiceActivityChange) {
        super(true, AUDIO.rec.sampleRate, lastActivityEvent);

        this.modelUri = modelUri;
        this.buffer = new Float32Array(AUDIO.vad.neuralFrameSamples + AUDIO.vad.nnVadContextSamples).fill(0);
        this.resetInternal();

        // Multithreading requires Cross Origin Isolation, so we don't use it here. See:
        // - https://web.dev/articles/cross-origin-isolation-guide
        // ort.env.wasm.numThreads = 4;
        const ortWasmSimdPath = Versioning.mapPath(ortWasm);
        ort.env.wasm.numThreads = 1;
        ort.env.wasm.proxy = true;
        ort.env.wasm.simd = true;
        // ort.env.wasm.numThreads = Math.min(4, (typeof navigator !== "undefined" && navigator.hardwareConcurrency) || 4);
        ort.env.wasm.wasmPaths = {
            wasm: ortWasmSimdPath,
            mjs: ortMjs,
        }
    }

    public async init(): Promise<void> {
        let session = this.session;
        session ??= await ort.InferenceSession.create(this.modelUri.toString(), {
            enableCpuMemArena: false,
            // executionMode: 'parallel',
            // graphOptimizationLevel: 'basic',
            executionProviders: ['wasm']
        });
        this.session = session;
    }

    protected async appendChunkInternal(monoPcm: Float32Array): Promise<number[] | null> {
        const { state, context } = this;
        if (this.session == null) {
            // skip processing until initialized
            return null;
        }

        if (monoPcm.length % AUDIO.vad.neuralFrameSamples !== 0) {
            throw new Error(`appendChunk() accepts ${AUDIO.vad.neuralFrameSamples}*N sample audio windows only, but found ${monoPcm.length}.`);
        }

        const frames = monoPcm.length / AUDIO.vad.neuralFrameSamples;
        const inputTensor = new ort.Tensor(monoPcm, [frames, AUDIO.vad.neuralFrameSamples]);
        const feeds = { input: inputTensor, state: state, context: context } as const;
        const result = await this.session.run(feeds);
        const { output, stateN, contextN } = result as any;
        this.state = stateN as ort.Tensor;
        this.context = contextN as ort.Tensor;
        const out = output.data as Float32Array | number[];
        // output shape is [B,1] — take first B values
        const probs: number[] = new Array(frames);
        for (let i = 0; i < frames; i++) probs[i] = Number((out as any)[i]);
        return probs;
    }

    protected resetInternal(): void {
        this.state = new ort.Tensor(new Float32Array(2 * 1 * 128), [2, 1, 128]);
        this.context = new ort.Tensor(new Float32Array(AUDIO.vad.nnVadContextSamples), [1, AUDIO.vad.nnVadContextSamples]);
    }
}

// Helpers

function adjustChangeEventsToSeconds(event: VoiceActivityChange, sampleRate: number): VoiceActivityChange {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration == null ? undefined : event.duration / sampleRate
    };
}
