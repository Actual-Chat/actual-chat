import * as ort from 'onnxruntime-web';
import { dma } from 'moving-averages';
import { ExponentialMovingAverage } from './streamed-moving-average';
import wasmPath from 'onnxruntime-web/dist/ort-wasm.wasm';
import wasmThreadedPath from 'onnxruntime-web/dist/ort-wasm-threaded.wasm';
import wasmSimdPath from 'onnxruntime-web/dist/ort-wasm-simd.wasm';
import wasmSimdThreadedPath from 'onnxruntime-web/dist/ort-wasm-simd-threaded.wasm';

export type VoiceActivityKind = 'start' | 'end';

export class VoiceActivityChanged {
    public readonly kind: VoiceActivityKind;
    public readonly offset: number;
    public readonly duration?: number;
    public readonly speechProb: number;

    constructor(kind: VoiceActivityKind, offset: number, speechProb: number, duration?: number) {
        this.kind = kind;
        this.offset = offset;
        this.speechProb = speechProb;
        this.duration = duration;
    }
}

export function adjustChangeEventsToSeconds(data: VoiceActivityChanged[], sampleRate: number = 16000): VoiceActivityChanged[] {
    return data.map(vac => new VoiceActivityChanged(vac.kind, vac.offset / sampleRate, vac.speechProb, vac.duration === null ? null : vac.duration / sampleRate));
}

const SamplesPerWindow = 2000;
const BytesPerSample = 4;
const AccumulativePeriod = 50;

export class VoiceActivityDetector {
    private readonly _modelUri: URL;
    private readonly _buffer: ArrayBuffer;
    private readonly _tensorBuffer: ArrayBuffer;
    private readonly _movingAverages: ExponentialMovingAverage;
    private readonly _streamedMedian: StreamedMedian;

    private _session: ort.InferenceSession = null;
    private _sampleCount: number;
    private _lastActivityEvent: VoiceActivityChanged;
    private _endOffset: number = null;
    private _totalSteps = 0;
    private _speechProbabilityTrigger = 0.26;
    private _silenceProbabilityTrigger = 0.15;

    constructor(modelUri: URL) {
        this._modelUri = modelUri;

        this._buffer = new ArrayBuffer(SamplesPerWindow * BytesPerSample * 2);
        this._tensorBuffer = new ArrayBuffer(SamplesPerWindow * BytesPerSample * 8);
        this._movingAverages = new ExponentialMovingAverage(8);
        this._streamedMedian = new StreamedMedian();

        this._sampleCount = 0;
        this._lastActivityEvent = new VoiceActivityChanged('end', 0, 0);

        ort.env.wasm.numThreads = 4;
        ort.env.wasm.simd = true;
        ort.env.wasm.wasmPaths = {
            'ort-wasm.wasm': wasmPath,
            'ort-wasm-threaded.wasm': wasmThreadedPath,
            'ort-wasm-simd.wasm': wasmSimdPath,
            'ort-wasm-simd-threaded.wasm': wasmSimdThreadedPath,
        };
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChanged[]> {
        if (this._session === null) {
            this._session = await ort.InferenceSession.create(this._modelUri.toString(), {
                enableCpuMemArena: false,
                executionMode: 'parallel',
                graphOptimizationLevel: 'basic',
                executionProviders: ['wasm']
            });
        }
        const session = this._session;
        const buffer = this._buffer;

        const stepCount = 8;
        const batchSize = stepCount;
        const step = SamplesPerWindow / stepCount;

        if (monoPcm.length !== SamplesPerWindow) {
            throw new Error("appendChunk() accepts 4000 sample audio windows only.");
        }

        const prevWindow = new Float32Array(buffer, 0, SamplesPerWindow);
        const currentWindow = new Float32Array(buffer, SamplesPerWindow * BytesPerSample, SamplesPerWindow);
        prevWindow.set(currentWindow, 0);
        currentWindow.set(monoPcm, 0);

        const batches: Float32Array[] = [];
        const results = [];
        const tensorSource = new Float32Array(this._tensorBuffer, 0, batchSize * SamplesPerWindow);
        for (let i = 0; i < monoPcm.length; i += step) {
            let chunk = new Float32Array(buffer, i * 4, SamplesPerWindow);
            batches.push(chunk);
            if (batches.length >= batchSize) {
                for (let j = 0; j < batches.length; j++) {
                    tensorSource.set(batches[j], j * SamplesPerWindow);
                }
                const tensor = new ort.Tensor(tensorSource, [batchSize, SamplesPerWindow]);
                const feeds = { input: tensor };
                const result = await session.run(feeds);
                results.push(result);
                batches.length = 0;
            }
        }

        const speechProbabilities = new Float32Array(batchSize * results.length);
        let probIndex = 0;
        for (let i = 0; i < results.length; i++) {
            const resultData = results[i].output.data as Float32Array;
            for (let j = 1; j < resultData.length; j += 2) {
                speechProbabilities[probIndex++] = resultData[j];
            }
        }
        const minSilenceSamples = 18000;
        const minSpeechSamples = 12000;
        const padSamples = 500;

        let trigSum = this._speechProbabilityTrigger;
        let negTrigSum = this._silenceProbabilityTrigger;

        const voiceActivityChangeEvents = new Array<VoiceActivityChanged>();
        const smoothedProbs = this._movingAverages.appendChunk(speechProbabilities);
        let currentEvent = this._lastActivityEvent;
        let endResetCounter = 0;

        for (let i = 0; i < smoothedProbs.length; i++) {
            const prob = speechProbabilities[i];
            const smoothedProb = smoothedProbs[i];
            const currentOffset = this._sampleCount + step * i;
            const probMedian = this._streamedMedian.push(prob);

            if (this._totalSteps++ >= AccumulativePeriod) {
                // enough statistics to adjust trigSum \ negTrigSum

                trigSum = 0.89 * probMedian + 0.08; // 0.08 when median is zero, 0.97 when median is 1
                negTrigSum = 0.3 * probMedian;
            }

            if (smoothedProb >= trigSum && this._endOffset > 0) {
                if (endResetCounter++ > 10) {
                    // silence period is too short
                    this._endOffset = null;
                    endResetCounter = 0;
                }
            }

            if ((smoothedProb >= trigSum || prob > 0.95) && currentEvent.kind === 'end') {
                const offset = Math.max(0, currentOffset - padSamples);
                const duration = offset - currentEvent.offset;
                currentEvent = new VoiceActivityChanged('start', offset, smoothedProb, duration);
                voiceActivityChangeEvents.push(currentEvent);
                continue;
            }
            if (smoothedProb < negTrigSum && currentEvent.kind === 'start') {
                endResetCounter = 0;
                if (this._endOffset === null) {
                    this._endOffset = currentOffset;
                }
                if (currentOffset - this._endOffset < minSilenceSamples) {
                    // too short
                } else {
                    const currentSpeechSamples = currentOffset - currentEvent.offset;
                    if (currentSpeechSamples > minSpeechSamples) {
                        const offset = this._endOffset + padSamples;
                        const duration = offset - currentEvent.offset
                        currentEvent = new VoiceActivityChanged('end', offset, smoothedProb, duration);
                        voiceActivityChangeEvents.push(currentEvent);
                        this._endOffset = null;
                    } else {
                        if (voiceActivityChangeEvents.length > 0) {
                            voiceActivityChangeEvents.pop();

                            if (voiceActivityChangeEvents.length > 0) {
                                currentEvent = voiceActivityChangeEvents[voiceActivityChangeEvents.length - 1];
                            } else {
                                currentEvent = this._lastActivityEvent;
                            }
                        }
                    }
                }
            }
        }
        this._sampleCount += monoPcm.length;
        this._lastActivityEvent = currentEvent;

        return [...voiceActivityChangeEvents];
    }
}

class StreamedMedian {
    private readonly _counts: Int32Array;
    private _totalValues: number;

    constructor() {
        this._counts = new Int32Array(100).fill(0);
        for (let index = 0; index < this._counts.length; index++) {
            this._counts[index] = 0;
        }
        this._median = 0;
        this._totalValues = 0;
    }

    private _median: number;

    public get median(): number {
        return this._median;
    }

    public push(value: number): number {
        this._totalValues++;
        const index = Math.round(Math.abs(value) * 100);
        this._counts[index]++;
        let sumOfCounts = 0;
        for (let j = 0; j < this._counts.length; j++) {
            const count = this._counts[j];
            sumOfCounts += count;
            if (sumOfCounts >= this._totalValues / 2) {
                this._median = j / 100;
                break;
            }
        }

        return this._median;
    }

}
