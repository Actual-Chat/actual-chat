import * as ort from 'onnxruntime-web';
import { ExponentialMovingAverage } from './streamed-moving-average';
import wasm from 'onnxruntime-web/dist/ort-wasm.wasm';
import wasmThreaded from 'onnxruntime-web/dist/ort-wasm-threaded.wasm';
import wasmSimd from 'onnxruntime-web/dist/ort-wasm-simd.wasm';
import wasmSimdThreaded from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm';
import { Versioning } from 'versioning';
import { VoiceActivityChange, VoiceActivityDetector } from './audio-vad-contract';

const MIN_SILENCE = 1; // 1s
const MIN_SPEECH = 0.5; // 500ms
const MAX_SPEECH = 60 * 2; // 2m

function adjustChangeEventsToSeconds(event: VoiceActivityChange, sampleRate): VoiceActivityChange {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration === null ? null : event.duration / sampleRate
    };
}

export abstract class VoiceActivityDetectorBase implements VoiceActivityDetector {
    protected readonly movingAverages: ExponentialMovingAverage;
    protected readonly longMovingAverages: ExponentialMovingAverage;
    protected readonly speechBoundaries: StreamedMedian;
    protected readonly minSilenceSamples;
    protected readonly minSpeechSamples;
    protected readonly maxSpeechSamples;
    // private readonly results =  new Array<number>();

    protected sampleCount = 0;
    protected lastActivityEvent: VoiceActivityChange;
    protected endOffset?: number = null;
    protected speechSteps = 0;
    protected speechProbabilities: StreamedMedian | null = null;
    protected triggeredSpeechProbability: number | null = null;
    protected endResetCounter: number = 0;

    protected constructor(protected sampleRate: number, private isHighQuality: boolean) {
        this.movingAverages = new ExponentialMovingAverage(8); // 32ms*8 ~ 250ms
        this.longMovingAverages = new ExponentialMovingAverage(64); // 32ms*64 ~ 2s
        this.speechBoundaries = new StreamedMedian();
        // this.speechBoundaries.push(0.75); // initial speech probability boundary
        this.speechBoundaries.push(0.5); // initial speech probability boundary
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };
        this.minSilenceSamples = sampleRate * MIN_SILENCE;
        this.minSpeechSamples = sampleRate * MIN_SPEECH;
        this.maxSpeechSamples = sampleRate * MAX_SPEECH;
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | null> {
        const {
            movingAverages,
            longMovingAverages,
            speechBoundaries,
            sampleRate,
            minSilenceSamples,
            minSpeechSamples,
            maxSpeechSamples,
            isHighQuality,
        } = this;
        let currentEvent = this.lastActivityEvent;
        const gain = this.calculateChunkGainApproximately(monoPcm);
        if (gain < 0.0025 && currentEvent.kind === 'end')
            return null; // do not try to check VAD at low gain input

        const prob = await this.appendChunkInternal(monoPcm);
        if (prob === null)
            return null; // skip processing until initialized

        // this.results.push(prob);
        const avgProb = movingAverages.append(prob);
        const longAvgProb = longMovingAverages.append(prob);
        const currentOffset = this.sampleCount;

        const probMedian = speechBoundaries.median;
        const speechProbabilityTrigger = 0.67 * probMedian;
        const silenceProbabilityTrigger = 0.15 * probMedian;

        if (currentEvent.kind === 'end' && avgProb >= longAvgProb && (isHighQuality ? prob >= speechProbabilityTrigger : avgProb >= speechProbabilityTrigger)) {
            // optimistic speech boundary detection
            const offset = Math.max(0, currentOffset - monoPcm.length);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: avgProb, duration };

            this.speechProbabilities = new StreamedMedian();
            this.triggeredSpeechProbability = prob;
        }
        else if (currentEvent.kind === 'start' && avgProb < longAvgProb && avgProb < silenceProbabilityTrigger) {
            this.endResetCounter = 0;
            if (this.endOffset === null) {
                // set end of speech boundary - will be cleaned up if speech begins again
                this.endOffset = currentOffset;
            }

            if (this.speechProbabilities !== null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                const offset = currentOffset + monoPcm.length;
                const duration = offset - currentEvent.offset
                const durationS = duration / sampleRate;
                // const speechMedian = this.speechProbabilities.median;
                const speechPercentage = this.speechProbabilities.count / (duration / monoPcm.length);
                if (this.triggeredSpeechProbability > 0.6 && speechPercentage > 0.5 &&  durationS > 2) {
                    this.speechBoundaries.push(this.triggeredSpeechProbability)
                }
                this.speechProbabilities = null;
                this.triggeredSpeechProbability = null;
            }
        }

        if (currentEvent.kind === 'start') {
            const currentSpeechSamples = currentOffset - currentEvent.offset;

            if (this.endOffset > 0) {
                // silence boundary has been detected
                if (avgProb >= speechProbabilityTrigger) {
                    if (this.endResetCounter++ > 10) {
                        // silence period is too short - cleanup silence boundary
                        this.endOffset = null;
                        this.endResetCounter = 0;
                    }
                } else if (avgProb < silenceProbabilityTrigger) {
                    const currentSilenceSamples = currentOffset - this.endOffset;
                    if (currentSilenceSamples > minSilenceSamples && currentSpeechSamples > minSpeechSamples) {
                        const offset = this.endOffset + monoPcm.length;
                        const duration = offset - currentEvent.offset;
                        currentEvent = { kind: 'end', offset, speechProb: avgProb, duration };
                        this.endOffset = null;
                    }
                }
            }

            if (currentEvent.kind === 'start' && currentSpeechSamples > maxSpeechSamples) {
                // break long speech regardless speech probability
                const offset = this.endOffset ?? currentOffset;
                const duration = offset - currentEvent.offset;
                currentEvent = { kind: 'end', offset: currentOffset, speechProb: avgProb, duration };
                this.endOffset = null;
            }

            if (this.speechProbabilities !== null && prob > 0.08) {
                this.speechProbabilities.push(prob);
            }
        }

        this.sampleCount += monoPcm.length;
        if (this.lastActivityEvent == currentEvent || this.lastActivityEvent.kind == currentEvent.kind) {
            return null;
        }

        this.lastActivityEvent = currentEvent;
        // uncomment for debugging
        // console.log(movingAverages.lastAverage, longMovingAverages.lastAverage, speechBoundaries.median, [...this.results], gain);
        // this.results.length = 0;
        return adjustChangeEventsToSeconds(currentEvent, this.sampleRate);
    }

    public abstract init(): Promise<void>;

    public reset(): void {
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };
        this.sampleCount = 0;
        this.endOffset = null;
        this.speechSteps = 0;
        this.endResetCounter = 0;
        this.resetInternal && this.resetInternal();
    }

    protected resetInternal?(): void;
    protected abstract appendChunkInternal(monoPcm: Float32Array): Promise<number|null>;

    private calculateChunkGainApproximately(monoPcm: Float32Array): number {
        let sum = 0;
        // every 5th sample as usually it's enough to assess speech gain
        for (let i = 0;i < monoPcm.length; i+=5) {
            const e = monoPcm[i];
            sum += e * e;
        }
        return Math.sqrt(sum / (monoPcm.length / 5));
    }
}

const SAMPLES_PER_WINDOW_16K = 512;
export class NNVoiceActivityDetector extends VoiceActivityDetectorBase {
    private readonly modelUri: URL;

    private session: ort.InferenceSession = null;
    private h0: ort.Tensor;
    private c0: ort.Tensor;

    constructor(modelUri: URL) {
        super(16000, true);

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

        if (monoPcm.length !== SAMPLES_PER_WINDOW_16K) {
            throw new Error(`appendChunk() accepts ${SAMPLES_PER_WINDOW_16K} sample audio windows only.`);
        }

        const tensor = new ort.Tensor(monoPcm, [1, SAMPLES_PER_WINDOW_16K]);
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

export class StreamedMedian {
    private readonly counts: Int32Array;
    private _count: number;
    private _median: number;

    constructor() {
        this.counts = new Int32Array(100).fill(0);
        for (let index = 0; index < this.counts.length; index++) {
            this.counts[index] = 0;
        }
        this._median = 0;
        this._count = 0;
    }

    public get median(): number {
        return this._median;
    }

    public get count(): number {
        return this._count;
    }

    public push(value: number): number {
        this._count++;
        const index = Math.round(Math.abs(value) * 100);
        this.counts[index]++;
        let sumOfCounts = 0;
        for (let j = 0; j < this.counts.length; j++) {
            const count = this.counts[j];
            sumOfCounts += count;
            if (sumOfCounts >= this._count / 2) {
                this._median = j / 100;
                break;
            }
        }
        return this._median;
    }
}
