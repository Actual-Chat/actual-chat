import { AUDIO_REC as AR, AUDIO_VAD as AV } from '_constants';
import { clamp, lerp, RunningUnitMedian, RunningEMA } from 'math';
import { Versioning } from 'versioning';
import * as ort from 'onnxruntime-web';
import wasm from 'onnxruntime-web/dist/ort-wasm.wasm';
import wasmThreaded from 'onnxruntime-web/dist/ort-wasm-threaded.wasm';
import wasmSimd from 'onnxruntime-web/dist/ort-wasm-simd.wasm';
import wasmSimdThreaded from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm';
import { VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';
import { Log } from 'logging';

const { logScope, debugLog } = Log.get('AudioVadWorker');

export const noVoiceActivity: VoiceActivityChange = { kind: 'end', offset: 0, speechProb: 0 };

export abstract class VoiceActivityDetectorBase implements VoiceActivityDetector {
    protected readonly probEMA = new RunningEMA(0.5, 5); // 32ms*5 ~ 150ms
    protected readonly longProbEMA = new RunningEMA(0.5, 64); // 32ms*64 ~ 2s
    protected readonly probMedian = new RunningUnitMedian();
    protected readonly minSpeechSamples: number;
    protected readonly maxSpeechSamples: number;
    protected readonly silenceThresholdVariesFromSamples: number;
    // private readonly results =  new Array<number>();

    protected sampleCount = 0;
    protected endOffset?: number = null;
    protected lastTriggeredProb: number | null = null;
    protected endResetCounter: number = 0;
    protected lastConversationSignalAtSample: number | null = null;
    protected silenceThresholdSamples: number;
    protected whenTalkingProbMedian: RunningUnitMedian | null = null;

    protected constructor(
        private isNeural: boolean,
        protected sampleRate: number,
        public lastActivityEvent: VoiceActivityChange = noVoiceActivity,
    ) {
        this.minSpeechSamples = sampleRate * AV.MIN_SPEECH;
        this.maxSpeechSamples = sampleRate * AV.MAX_SPEECH;
        this.silenceThresholdVariesFromSamples = sampleRate * AV.SILENCE_THRESHOLD_VARIES_FROM;
        this.silenceThresholdSamples = sampleRate * AV.MAX_SILENCE;
    }

    public abstract init(): Promise<void>;

    public reset(): void {
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };
        this.sampleCount = 0;
        this.endOffset = null;
        this.endResetCounter = 0;
        this.lastConversationSignalAtSample = null;
        this.resetInternal && this.resetInternal();
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | number> {
        let currentEvent = this.lastActivityEvent;
        const gain = approximateGain(monoPcm);
        // debugLog?.log('appendChunk:', currentEvent, gain);
        if (gain < 0.0025 && currentEvent.kind === 'end')
            return gain; // do not try to check VAD at low gain input

        const prob = await this.appendChunkInternal(monoPcm);
        if (prob === null)
            return gain; // no voice activity probability yet

        // this.results.push(prob);
        this.probEMA.appendSample(prob);
        this.longProbEMA.appendSample(prob);
        const probEma = this.probEMA.value;
        const longProbEma = this.longProbEMA.value;
        const probMedian = this.probMedian.value;
        const currentOffset = this.sampleCount;
        const speechProbTrigger = 0.67 * probMedian;
        const silenceProbTrigger = 0.15 * probMedian;

        if (currentEvent.kind === 'end'
            && probEma >= longProbEma
            && ((this.isNeural ? prob : probEma) >= speechProbTrigger)
        ) {
            // Speech start detected
            const offset = Math.max(0, currentOffset - monoPcm.length);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: probEma, duration };
            this.lastTriggeredProb = prob;
            this.whenTalkingProbMedian = new RunningUnitMedian();
            this.silenceThresholdSamples = this.sampleRate * AV.MAX_SILENCE;
        }
        else if (currentEvent.kind === 'start' && probEma < longProbEma && probEma < silenceProbTrigger) {
            // Speech end detected
            this.endResetCounter = 0;
            if (this.endOffset === null)
                this.endOffset = currentOffset;

            if (this.whenTalkingProbMedian !== null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                const offset = currentOffset + monoPcm.length;
                const duration = offset - currentEvent.offset
                const durationS = duration / this.sampleRate;
                const speechRatio = this.whenTalkingProbMedian.sampleCount / (duration / monoPcm.length);
                if (this.lastTriggeredProb > 0.6 && speechRatio > 0.5 &&  durationS > 2)
                    this.probMedian.appendSample(this.lastTriggeredProb)
                this.whenTalkingProbMedian = null;
                this.lastTriggeredProb = null;
            }
        }

        if (currentEvent.kind === 'start') {
            const currentSpeechSamples = currentOffset - currentEvent.offset;

            if (this.endOffset > 0) {
                // silence boundary has been detected
                if (probEma >= speechProbTrigger) {
                    if (this.endResetCounter++ > 10) {
                        // silence period is too short - cleanup silence boundary
                        this.endOffset = null;
                        this.endResetCounter = 0;
                    }
                } else if (probEma < silenceProbTrigger) {
                    const currentSilenceSamples = currentOffset - this.endOffset;
                    if (currentSilenceSamples > this.silenceThresholdSamples && currentSpeechSamples > this.minSpeechSamples) {
                        const offset = this.endOffset + monoPcm.length;
                        const duration = offset - currentEvent.offset;
                        currentEvent = { kind: 'end', offset, speechProb: probEma, duration };
                        this.endOffset = null;
                    }
                }
            }

            if (currentEvent.kind === 'start' && currentSpeechSamples > this.maxSpeechSamples) {
                // break long speech regardless of speech probability
                const offset = this.endOffset ?? currentOffset;
                const duration = offset - currentEvent.offset;
                currentEvent = { kind: 'end', offset: currentOffset, speechProb: probEma, duration };
                this.endOffset = null;
            }

            if (this.whenTalkingProbMedian !== null && prob > 0.08)
                this.whenTalkingProbMedian.appendSample(prob);

            // adjust max silence for long speech - break more aggressively, but keep longer pauses for monologue
            const isConversation = this.lastConversationSignalAtSample !== null
                && (this.sampleCount - this.lastConversationSignalAtSample) <= this.sampleRate * AV.CONV_DURATION;
            const maxSilence = isConversation ? AV.MAX_CONV_SILENCE : AV.MAX_SILENCE;
            const silenceThresholdAlpha =
                (currentSpeechSamples - this.silenceThresholdVariesFromSamples) /
                (this.maxSpeechSamples - this.silenceThresholdVariesFromSamples);
            const silenceThreshold = lerp(maxSilence, AV.MIN_SILENCE, clamp(silenceThresholdAlpha, 0, 1));
            this.silenceThresholdSamples = Math.floor(this.sampleRate * silenceThreshold);
        }

        this.sampleCount += monoPcm.length;
        if (this.lastActivityEvent == currentEvent || this.lastActivityEvent.kind == currentEvent.kind)
            return gain;

        this.lastActivityEvent = currentEvent;
        // uncomment for debugging
        // console.log(movingAverages.lastAverage, longMovingAverages.lastAverage, speechBoundaries.median, [...this.results], gain);
        // this.results.length = 0;
        return adjustChangeEventsToSeconds(currentEvent, this.sampleRate);
    }

    public conversationSignal(): void {
        this.lastConversationSignalAtSample = this.sampleCount;
    }

    protected resetInternal?(): void;
    protected abstract appendChunkInternal(monoPcm: Float32Array): Promise<number|null>;
}

export class NNVoiceActivityDetector extends VoiceActivityDetectorBase {
    private readonly modelUri: URL;

    private session: ort.InferenceSession = null;
    private h0: ort.Tensor;
    private c0: ort.Tensor;

    constructor(modelUri: URL, lastActivityEvent: VoiceActivityChange) {
        super(true, AR.SAMPLE_RATE, lastActivityEvent);

        this.modelUri = modelUri;
        this.h0 = new ort.Tensor(new Float32Array(2 * 64), [2, 1, 64]);
        this.c0 = new ort.Tensor(new Float32Array(2 * 64), [2, 1, 64]);

        const wasmPath = Versioning.mapPath(wasm);
        const wasmThreadedPath = Versioning.mapPath(wasmThreaded);
        const wasmSimdPath = Versioning.mapPath(wasmSimd);
        const wasmSimdThreadedPath = Versioning.mapPath(wasmSimdThreaded);

        ort.env.wasm.numThreads = 4;
        ort.env.wasm.simd = true;
        ort.env.wasm.wasmPaths = {
            'ort-wasm.wasm': wasmPath,
            'ort-wasm-threaded.wasm': wasmThreadedPath,
            'ort-wasm-simd.wasm': wasmSimdPath,
            'ort-wasm-simd-threaded.wasm': wasmSimdThreadedPath,
        };
    }

    public async init(): Promise<void> {
        let session = this.session;
        if (session === null) {
            session = await ort.InferenceSession.create(this.modelUri.toString(), {
                enableCpuMemArena: false,
                executionMode: 'parallel',
                graphOptimizationLevel: 'basic',
                executionProviders: ['wasm']
            });
        }
        this.session = session;
    }

    protected async appendChunkInternal(monoPcm: Float32Array): Promise<number | null> {
        const { h0, c0} = this;
        if (this.session == null) {
            // skip processing until initialized
            return null;
        }

        if (monoPcm.length !== AR.SAMPLES_PER_WINDOW_32) {
            throw new Error(`appendChunk() accepts ${AR.SAMPLES_PER_WINDOW_32} sample audio windows only.`);
        }

        const tensor = new ort.Tensor(monoPcm, [1, AR.SAMPLES_PER_WINDOW_32]);
        const srArray = new BigInt64Array(1).fill(BigInt(16000));
        const sr = new ort.Tensor(srArray);
        const feeds = { input: tensor, h: h0, c: c0, sr: sr };
        const result = await this.session.run(feeds);
        const { output, hn, cn } = result;
        this.h0 = hn;
        this.c0 = cn;

        return output.data[0] as number;
    }
}

// Helpers

function approximateGain(monoPcm: Float32Array, stride = 5): number {
    let sum = 0;
    // every 5th sample as usually it's enough to assess speech gain
    for (let i = 0; i < monoPcm.length; i+=stride) {
        const e = monoPcm[i];
        sum += e * e;
    }
    return Math.sqrt(sum / Math.floor(monoPcm.length / stride));
}

function adjustChangeEventsToSeconds(event: VoiceActivityChange, sampleRate: number): VoiceActivityChange {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration === null ? null : event.duration / sampleRate
    };
}
