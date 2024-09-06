import { AUDIO_REC as AR, AUDIO_VAD as VC } from '_constants';
import { clamp, RunningUnitMedian, RunningEMA } from 'math';
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

    protected readonly speechProbEMA: RunningEMA;
    protected readonly longSpeechProbEMA: RunningEMA;
    protected readonly speechBoundaries: RunningUnitMedian;
    protected readonly minSpeechSamples: number;
    protected readonly maxSpeechSamples: number;
    // private readonly results =  new Array<number>();

    protected sampleCount = 0;
    protected endOffset?: number = null;
    protected speechSteps = 0;
    protected speechProbabilities: RunningUnitMedian | null = null;
    protected triggeredSpeechProbability: number | null = null;
    protected endResetCounter: number = 0;
    protected maxSilenceSamples: number;
    protected lastConversationSignalAtSample: number | null = null;

    protected constructor(
        private isNeural: boolean,
        protected sampleRate: number,
        public lastActivityEvent: VoiceActivityChange = noVoiceActivity,
    ) {
        this.speechProbEMA = new RunningEMA(5, 0.5); // 32ms*5 ~ 150ms
        this.longSpeechProbEMA = new RunningEMA(64, 0.5); // 32ms*64 ~ 2s
        this.speechBoundaries = new RunningUnitMedian();
        this.maxSilenceSamples = sampleRate * VC.MAX_SILENCE;
        this.minSpeechSamples = sampleRate * VC.MIN_SPEECH;
        this.maxSpeechSamples = sampleRate * VC.MAX_SPEECH;
    }

    public abstract init(): Promise<void>;

    public reset(): void {
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };
        this.sampleCount = 0;
        this.endOffset = null;
        this.speechSteps = 0;
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
        this.speechProbEMA.appendSample(prob);
        this.longSpeechProbEMA.appendSample(prob);
        const avgProb = this.speechProbEMA.value;
        const longAvgProb = this.longSpeechProbEMA.value;
        const currentOffset = this.sampleCount;
        const probMedian = this.speechBoundaries.value;
        const speechProbTrigger = 0.67 * probMedian;
        const silenceProbTrigger = 0.15 * probMedian;

        if (currentEvent.kind === 'end'
            && avgProb >= longAvgProb
            && (this.isNeural ? prob >= speechProbTrigger : avgProb >= speechProbTrigger)
        ) {
            // optimistic speech boundary detection
            const offset = Math.max(0, currentOffset - monoPcm.length);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: avgProb, duration };

            this.speechProbabilities = new RunningUnitMedian();
            this.triggeredSpeechProbability = prob;
            this.maxSilenceSamples = this.sampleRate * VC.MAX_SILENCE;
        }
        else if (currentEvent.kind === 'start' && avgProb < longAvgProb && avgProb < silenceProbTrigger) {
            this.endResetCounter = 0;
            if (this.endOffset === null) {
                // set end of speech boundary - will be cleaned up if speech begins again
                this.endOffset = currentOffset;
            }

            if (this.speechProbabilities !== null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                const offset = currentOffset + monoPcm.length;
                const duration = offset - currentEvent.offset
                const durationS = duration / this.sampleRate;
                // const speechMedian = this.speechProbabilities.median;
                const speechPercentage = this.speechProbabilities.sampleCount / (duration / monoPcm.length);
                if (this.triggeredSpeechProbability > 0.6 && speechPercentage > 0.5 &&  durationS > 2)
                    this.speechBoundaries.appendSample(this.triggeredSpeechProbability)
                this.speechProbabilities = null;
                this.triggeredSpeechProbability = null;
            }
        }

        if (currentEvent.kind === 'start') {
            const currentSpeechSamples = currentOffset - currentEvent.offset;

            if (this.endOffset > 0) {
                // silence boundary has been detected
                if (avgProb >= speechProbTrigger) {
                    if (this.endResetCounter++ > 10) {
                        // silence period is too short - cleanup silence boundary
                        this.endOffset = null;
                        this.endResetCounter = 0;
                    }
                } else if (avgProb < silenceProbTrigger) {
                    const currentSilenceSamples = currentOffset - this.endOffset;
                    if (currentSilenceSamples > this.maxSilenceSamples && currentSpeechSamples > this.minSpeechSamples) {
                        const offset = this.endOffset + monoPcm.length;
                        const duration = offset - currentEvent.offset;
                        currentEvent = { kind: 'end', offset, speechProb: avgProb, duration };
                        this.endOffset = null;
                    }
                }
            }

            if (currentEvent.kind === 'start' && currentSpeechSamples > this.maxSpeechSamples) {
                // break long speech regardless of speech probability
                const offset = this.endOffset ?? currentOffset;
                const duration = offset - currentEvent.offset;
                currentEvent = { kind: 'end', offset: currentOffset, speechProb: avgProb, duration };
                this.endOffset = null;
            }

            if (this.speechProbabilities !== null && prob > 0.08)
                this.speechProbabilities.appendSample(prob);

            // adjust max silence for long speech - break more aggressively, but keep longer pauses for monologue
            const isMonologue = !this.lastConversationSignalAtSample
                || (this.sampleCount - this.lastConversationSignalAtSample) > this.sampleRate * VC.NON_MONOLOGUE_DURATION;
            const maxSilence = isMonologue
                ? VC.MAX_MONOLOGUE_SILENCE
                : VC.MAX_SILENCE;
            const startAdjustingMaxSilenceSamples = this.sampleRate * VC.MAX_SILENCE_VARIES_FROM;
            this.maxSilenceSamples = Math.floor(this.sampleRate
                * ((maxSilence - VC.MIN_SILENCE)
                    * clamp(1 - (currentSpeechSamples - startAdjustingMaxSilenceSamples) / (this.maxSpeechSamples - startAdjustingMaxSilenceSamples), 0, 1)
                    + VC.MIN_SILENCE));
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
