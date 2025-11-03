import { AUDIO_REC as AR, AUDIO_VAD as AV } from '_constants';
import { clamp, lerp, RunningUnitMedian, RunningEMA, approximateGain } from 'math';
import { ResolvedPromise } from 'promises';
// @ts-ignore
// import * as ort from 'onnxruntime-web';
import * as ort from 'onnxruntime-web/wasm';
import ortMjs from './ort-wasm-simd.mjs'
import ortWasm from './ort-wasm-simd.wasm'
// import ortWasm from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm'
// import ortMjs from 'onnxruntime-web/dist/ort-wasm-simd-threaded.mjs'
import { WebRtcVad } from '@actual-chat/webrtc-vad';
import { VoiceActivityChange, VoiceActivityDetector, NO_VOICE_ACTIVITY } from './audio-vad-contract';
// import { Log } from 'logging';
import { Versioning } from 'versioning';

// const { debugLog } = Log.get('AudioVadWorker');

export abstract class VoiceActivityDetectorBase implements VoiceActivityDetector {
    protected readonly probEMA = new RunningEMA(0.5, 5); // 32ms*5 ~ 150ms
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
        this.minSpeechSamples = sampleRate * AV.MIN_SPEECH;
        this.maxSpeechSamples = sampleRate * AV.MAX_SPEECH;
        this.maxPauseSamples = sampleRate * AV.MAX_PAUSE;
        this.maxPauseCancelSamples = sampleRate * AV.MIN_SPEECH_TO_CANCEL_PAUSE;
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
        const windowSamples = this.isNeural ? AR.SAMPLES_PER_WINDOW_32 : AR.SAMPLES_PER_WINDOW_30;
        if (monoPcm.length % windowSamples !== 0)
            throw new Error(`appendChunk() accepts ${windowSamples}*N sample audio windows only, but found ${monoPcm.length}.`);

        const frames = monoPcm.length / windowSamples;
        const startOffset = this.sampleCount;
        this.sampleCount += monoPcm.length;

        // Query probabilities in one shot (for neural) or per-frame (for WebRTC)
        const probs = await this.appendChunkInternal(monoPcm);
        // If not initialized yet
        if (probs === null)
            return approximateGain(monoPcm.subarray(0, windowSamples));

        let currentEvent = this.lastActivityEvent;
        let lastReturn: VoiceActivityChange | number = 0;

        for (let i = 0; i < frames; i++) {
            const frameOffset = startOffset + i * windowSamples;
            const frame = monoPcm.subarray(i * windowSamples, (i + 1) * windowSamples);
            const gain = approximateGain(frame);
            if (gain < AR.MIN_RECORDING_GAIN && currentEvent.kind === 'end') {
                lastReturn = gain; // do not try to check VAD at low gain input
                continue;
            }

            const prob = Array.isArray(probs) ? probs[i] : (probs as unknown as number);
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
                this.maxPauseSamples = this.sampleRate * AV.MAX_PAUSE;
            }
            else if (currentEvent.kind === 'start' && probEma < longProbEma && probEma < pauseProbTrigger) {
                // pause start detected
                this.pauseCancelSamples = 0;
                if (this.pauseOffset === null)
                    this.pauseOffset = frameOffset;
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
                    && (this.sampleCount - this.lastConversationSignalAtSample) <= this.sampleRate * AV.CONV_DURATION;
                const maxPause = isConversation ? AV.MAX_CONV_PAUSE : AV.MAX_PAUSE;
                const maxPauseVariesFromSamples = this.sampleRate * AV.PAUSE_VARIES_FROM;
                let maxPauseAlpha =
                    (currentSpeechSamples - maxPauseVariesFromSamples) /
                    (this.maxSpeechSamples - maxPauseVariesFromSamples); // Always > 0
                maxPauseAlpha = clamp(maxPauseAlpha, 0, 1);
                maxPauseAlpha = Math.pow(maxPauseAlpha, AV.PAUSE_VARY_POWER);
                const silenceThreshold = lerp(maxPause, AV.MIN_PAUSE, maxPauseAlpha);
                this.maxPauseSamples = Math.floor(this.sampleRate * silenceThreshold);
            }

            if (this.lastActivityEvent == currentEvent || this.lastActivityEvent.kind == currentEvent.kind) {
                lastReturn = gain;
            } else {
                this.lastActivityEvent = currentEvent;
                lastReturn = adjustChangeEventsToSeconds(currentEvent, this.sampleRate);
            }
        }

        return lastReturn;
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
    private lastSkippedAt: number = 0;

    constructor(private baseVad: WebRtcVad) {
        super(false, AR.SAMPLE_RATE);
    }

    public override init(): Promise<void> {
        return ResolvedPromise.Void;
    }

    protected override appendChunkInternal(monoPcm: Float32Array): Promise<number[] | null> {
        if (monoPcm.length % AR.SAMPLES_PER_WINDOW_30 !== 0)
            throw new Error(`appendChunk() accepts ${AR.SAMPLES_PER_WINDOW_30}*N sample audio windows only.`);

        const frames = monoPcm.length / AR.SAMPLES_PER_WINDOW_30;
        const probs: number[] = [];
        for (let i = 0; i < frames; i++) {
            const frame = monoPcm.subarray(i * AR.SAMPLES_PER_WINDOW_30, (i + 1) * AR.SAMPLES_PER_WINDOW_30);
            // Emscripten interop requires Uint8Array or ArrayBuffer, so we need to pass Float32Array as Uint8Array
            const activity = this.baseVad.detect(new Uint8Array(frame.buffer, frame.byteOffset, frame.length * 4));
            const now = Date.now();
            if (activity > 0 && (this.sampleCount / AR.SAMPLES_PER_MS < AV.SKIP_FIRST_RECORDING_MS || now - this.lastSkippedAt < 5)) {
                this.lastSkippedAt = now;
                return Promise.resolve(null); // Skip triggering on microphone noise at the beginning of the first microphone stream
            }
            if (activity == VadActivity.Error)
                throw new Error(`Error calling WebRtc VAD`);
            // Map 1|0 to ~[0,0.8]
            probs.push(Number(0.8 * activity));
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
        super(true, AR.SAMPLE_RATE, lastActivityEvent);

        this.modelUri = modelUri;
        this.buffer = new Float32Array(AR.SAMPLES_PER_WINDOW_32 + AV.NN_VAD_CONTEXT_SAMPLES).fill(0);
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
        if (session === null) {
            session = await ort.InferenceSession.create(this.modelUri.toString(), {
                enableCpuMemArena: false,
                // executionMode: 'parallel',
                // graphOptimizationLevel: 'basic',
                executionProviders: ['wasm']
            });
        }
        this.session = session;
    }

    protected async appendChunkInternal(monoPcm: Float32Array): Promise<number[] | null> {
        const { state, context } = this;
        if (this.session == null) {
            // skip processing until initialized
            return null;
        }

        if (monoPcm.length % AR.SAMPLES_PER_WINDOW_32 !== 0) {
            throw new Error(`appendChunk() accepts ${AR.SAMPLES_PER_WINDOW_32}*N sample audio windows only, but found ${monoPcm.length}.`);
        }

        const frames = monoPcm.length / AR.SAMPLES_PER_WINDOW_32;
        const inputTensor = new ort.Tensor(monoPcm, [frames, AR.SAMPLES_PER_WINDOW_32]);
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
        this.context = new ort.Tensor(new Float32Array(AV.NN_VAD_CONTEXT_SAMPLES), [1, AV.NN_VAD_CONTEXT_SAMPLES]);
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
