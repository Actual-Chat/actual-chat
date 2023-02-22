import * as ort from 'onnxruntime-web';
import { ExponentialMovingAverage } from './streamed-moving-average';
import wasmPath from 'onnxruntime-web/dist/ort-wasm.wasm';
import wasmThreadedPath from 'onnxruntime-web/dist/ort-wasm-threaded.wasm';
import wasmSimdPath from 'onnxruntime-web/dist/ort-wasm-simd.wasm';
import wasmSimdThreadedPath from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm';
import { LogScope } from 'logging';

const LogScope: LogScope = 'AudioVad';

const SAMPLE_RATE = 16000;
const MIN_SILENCE_SAMPLES = 8000; // 500ms
const MIN_SPEECH_SAMPLES = 8000; // 500ms
const MAX_SPEECH_SAMPLES = 16000 * 60 * 2; // 2m
const PAD_SAMPLES = 512;
const SAMPLES_PER_WINDOW = 512; // 32ms

export type VoiceActivityKind = 'start' | 'end';

export interface VoiceActivityChange {
    kind: VoiceActivityKind;
    offset: number;
    duration?: number;
    speechProb: number;
}

export function adjustChangeEventsToSeconds(event: VoiceActivityChange, sampleRate = SAMPLE_RATE): VoiceActivityChange {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration === null ? null : event.duration / sampleRate
    };
}

export class VoiceActivityDetector {
    private readonly modelUri: URL;
    private readonly movingAverages: ExponentialMovingAverage;
    private readonly longMovingAverages: ExponentialMovingAverage;
    private readonly speechBoundaries: StreamedMedian ;

    private session: ort.InferenceSession = null;
    private sampleCount = 0;
    private lastActivityEvent: VoiceActivityChange;
    private endOffset?: number = null;
    private speechSteps = 0;
    private speechProbabilities: StreamedMedian | null = null;
    private triggeredSpeechProbability: number | null = null;
    private endResetCounter: number = 0;
    private h0: ort.Tensor;
    private c0: ort.Tensor;


    constructor(modelUri: URL) {
        this.modelUri = modelUri;

        this.movingAverages = new ExponentialMovingAverage(8); // 32ms*8 ~ 250ms
        this.longMovingAverages = new ExponentialMovingAverage(128); // 32ms*128 ~ 4s
        this.speechBoundaries = new StreamedMedian();
        this.speechBoundaries.push(0.5); // initial speech probability boundary
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };

        this.h0 = new ort.Tensor(new Float32Array(2 * 64), [2, 1, 64]);
        this.c0 = new ort.Tensor(new Float32Array(2 * 64), [2, 1, 64]);

        ort.env.wasm.numThreads = 4;
        ort.env.wasm.simd = true;
        ort.env.wasm.wasmPaths = {
            'ort-wasm.wasm': wasmPath as string,
            'ort-wasm-threaded.wasm': wasmThreadedPath as string,
            'ort-wasm-simd.wasm': wasmSimdPath as string,
            'ort-wasm-simd-threaded.wasm': wasmSimdThreadedPath as string,
        };
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | null> {
        const { movingAverages, speechBoundaries, h0, c0 } = this;
        if (this.session == null) {
            // skip processing until initialized
            return null;
        }

        if (monoPcm.length !== SAMPLES_PER_WINDOW) {
            throw new Error(`appendChunk() accepts ${SAMPLES_PER_WINDOW} sample audio windows only.`);
        }

        const tensor = new ort.Tensor(monoPcm, [1, SAMPLES_PER_WINDOW]);
        const srArray = new BigInt64Array(1).fill(BigInt(16000));
        const sr = new ort.Tensor(srArray);
        const feeds = { input: tensor, h: h0, c: c0, sr: sr };
        const result = await this.session.run(feeds);
        const { output, hn, cn } = result;
        this.h0 = hn;
        this.c0 = cn;

        const prob: number = output.data[0] as number;
        const avgProb = movingAverages.append(prob);
        const longAvgProb = this.longMovingAverages.append(prob);
        let currentEvent = this.lastActivityEvent;
        const currentOffset = this.sampleCount;

        const probMedian = speechBoundaries.median;
        const speechProbabilityTrigger = 0.67 * probMedian;
        const silenceProbabilityTrigger = 0.15 * probMedian;

        if (currentEvent.kind === 'end' && avgProb >= longAvgProb && prob >= speechProbabilityTrigger) {
            // optimistic speech boundary detection
            const offset = Math.max(0, currentOffset - PAD_SAMPLES);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: avgProb, duration };

            this.speechProbabilities = new StreamedMedian();
            this.triggeredSpeechProbability = prob;
        }
        else if (currentEvent.kind === 'start' && avgProb < longAvgProb && longAvgProb < silenceProbabilityTrigger) {
            this.endResetCounter = 0;
            if (this.endOffset === null) {
                // set end of speech boundary - will be cleaned up if speech begins again
                this.endOffset = currentOffset;
            }

            if (this.speechProbabilities !== null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                const offset = currentOffset + PAD_SAMPLES;
                const duration = offset - currentEvent.offset
                const durationS = duration / SAMPLE_RATE;
                const speechMedian = this.speechProbabilities.median;
                const speechPercentage = this.speechProbabilities.count / (duration / SAMPLES_PER_WINDOW);
                if (speechMedian > 0.6 && speechPercentage > 0.5 &&  durationS > 2 && this.triggeredSpeechProbability != null) {
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
                    if (currentSilenceSamples > MIN_SILENCE_SAMPLES && currentSpeechSamples > MIN_SPEECH_SAMPLES) {
                        const offset = this.endOffset + PAD_SAMPLES;
                        const duration = offset - currentEvent.offset;
                        currentEvent = { kind: 'end', offset, speechProb: avgProb, duration };
                        this.endOffset = null;
                    }
                }
            }

            if (currentEvent.kind === 'start' && currentSpeechSamples > MAX_SPEECH_SAMPLES) {
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
        return currentEvent;
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

    public reset(): void {
        this.lastActivityEvent = { kind: 'end', offset: 0, speechProb: 0 };
        this.sampleCount = 0;
        this.endOffset = null;
        this.speechSteps = 0;
        this.endResetCounter = 0;
    }
}

class StreamedMedian {
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
