import * as ort from 'onnxruntime-web';
import { ExponentialMovingAverage } from './streamed-moving-average';
import wasmPath from 'onnxruntime-web/dist/ort-wasm.wasm';
import wasmThreadedPath from 'onnxruntime-web/dist/ort-wasm-threaded.wasm';
import wasmSimdPath from 'onnxruntime-web/dist/ort-wasm-simd.wasm';
import wasmSimdThreadedPath from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm';

export type VoiceActivityKind = 'start' | 'end';

export interface VoiceActivityChanged {
    kind: VoiceActivityKind;
    offset: number;
    duration?: number;
    speechProb: number;
}

export function adjustChangeEventsToSeconds(event: VoiceActivityChanged, sampleRate = 16000): VoiceActivityChanged {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration === null ? null : event.duration / sampleRate
    };
}

const MIN_SILENCE_SAMPLES = 8000;
const MAX_SILENCE_SAMPLES = 64000;
const MIN_SPEECH_SAMPLES = 8000;
const MAX_SPEECH_SAMPLES = 16000 * 60 * 2;
const PAD_SAMPLES = 512;
const SAMPLES_PER_WINDOW = 512;
const ACCUMULATIVE_PERIOD_START = 100;
const ACCUMULATIVE_PERIOD_END = 300;

export class VoiceActivityDetector {
    private readonly modelUri: URL;
    private readonly movingAverages: ExponentialMovingAverage;
    private readonly streamedMedian: StreamedMedian;
    private readonly initPromise: Promise<void>;

    private session: ort.InferenceSession = null;
    private sampleCount = 0;
    private lastActivityEvent: VoiceActivityChanged;
    private endOffset: number = null;
    private speechSteps = 0;
    private endResetCounter = 0;
    private speechProbabilityTrigger = 0.26;
    private silenceProbabilityTrigger = 0.15;
    private h0: ort.Tensor;
    private c0: ort.Tensor;


    constructor(modelUri: URL, initOnCreate = false) {
        this.modelUri = modelUri;

        this.movingAverages = new ExponentialMovingAverage(8);
        this.streamedMedian = new StreamedMedian();
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

        if (initOnCreate) {
            this.initPromise = this.init();
        }
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChanged> {
        const { movingAverages, streamedMedian, h0, c0 } = this;
        if (this.session == null) {
            if (this.initPromise) {
                await this.initPromise;
            }
            else {
                await this.init();
            }
        }

        if (monoPcm.length !== SAMPLES_PER_WINDOW) {
            throw new Error(`appendChunk() accepts ${SAMPLES_PER_WINDOW} sample audio windows only.`);
        }

        const tensor = new ort.Tensor(monoPcm, [1, SAMPLES_PER_WINDOW]);
        const feeds = { input: tensor, h0: h0, c0: c0 };
        const result = await this.session.run(feeds);
        const { output, hn, cn } = result;
        this.h0 = hn;
        this.c0 = cn;

        let trigSum = this.speechProbabilityTrigger;
        let negTrigSum = this.silenceProbabilityTrigger;

        const prob: number = output.data[1] as number;
        const smoothedProb = movingAverages.append(prob);
        let currentEvent = this.lastActivityEvent;
        const currentOffset = this.sampleCount;

        if (this.speechSteps >= ACCUMULATIVE_PERIOD_START) {
            // enough statistics to adjust trigSum \ negTrigSum
            const probMedian = streamedMedian.median;
            this.speechProbabilityTrigger = trigSum = 0.80 * probMedian + 0.15; // 0.15 when median is zero, 0.95 when median is 1
            this.silenceProbabilityTrigger = negTrigSum = 0.3 * probMedian;
        }

        if (smoothedProb >= trigSum && this.endOffset > 0) {
            if (this.endResetCounter++ > 5) {
                // silence period is too short
                this.endOffset = null;
                this.endResetCounter = 0;
            }
        }

        if ((smoothedProb >= trigSum || prob > 0.95) && currentEvent.kind === 'end') {
            const offset = Math.max(0, currentOffset - PAD_SAMPLES);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: smoothedProb, duration };
            if (this.speechSteps++ < ACCUMULATIVE_PERIOD_END) {
                this.streamedMedian.push(prob);
            }
        }
        else if (smoothedProb < negTrigSum && currentEvent.kind === 'start') {
            this.endResetCounter = 0;
            if (this.endOffset === null) {
                this.endOffset = currentOffset;
            }
            const currentSpeechSamples = currentOffset - currentEvent.offset;
            const currentSilenceSamples = currentOffset - this.endOffset;
            if (currentSilenceSamples < MAX_SILENCE_SAMPLES) {
                if (currentSpeechSamples > MAX_SPEECH_SAMPLES) {
                    // try to break long speech at the nearest short pause
                    if (currentSilenceSamples > MIN_SILENCE_SAMPLES) {
                        const offset = this.endOffset + PAD_SAMPLES;
                        const duration = offset - currentEvent.offset
                        currentEvent = { kind: 'end', offset, speechProb: smoothedProb, duration };
                        this.endOffset = null;
                    }
                }
                // do nothing - current silence period is too short
            } else {
                if (currentSpeechSamples > MIN_SPEECH_SAMPLES) {
                    const offset = this.endOffset + PAD_SAMPLES;
                    const duration = offset - currentEvent.offset
                    currentEvent = { kind: 'end', offset, speechProb: smoothedProb, duration };
                    this.endOffset = null;
                }
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
    private totalValues: number;

    constructor() {
        this.counts = new Int32Array(100).fill(0);
        for (let index = 0; index < this.counts.length; index++) {
            this.counts[index] = 0;
        }
        this._median = 0;
        this.totalValues = 0;
    }

    private _median: number;

    public get median(): number {
        return this._median;
    }

    public push(value: number): number {
        this.totalValues++;
        const index = Math.round(Math.abs(value) * 100);
        this.counts[index]++;
        let sumOfCounts = 0;
        for (let j = 0; j < this.counts.length; j++) {
            const count = this.counts[j];
            sumOfCounts += count;
            if (sumOfCounts >= this.totalValues / 2) {
                this._median = j / 100;
                break;
            }
        }
        return this._median;
    }
}
