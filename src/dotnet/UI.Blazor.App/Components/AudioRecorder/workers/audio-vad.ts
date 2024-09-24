
import { AUDIO_REC as AR, AUDIO_VAD as AV } from '_constants';
import { clamp, lerp, RunningUnitMedian, RunningEMA, approximateGain } from 'math';
// @ts-ignore - it works, but fails validation
import * as ort from 'onnxruntime-web/wasm';
import { WebRtcVad } from '@actual-chat/webrtc-vad';
import { VoiceActivityChange, VoiceActivityDetector, NO_VOICE_ACTIVITY } from './audio-vad-contract';
import { Log } from 'logging';

const { debugLog } = Log.get('AudioVadWorker');

export abstract class VoiceActivityDetectorBase implements VoiceActivityDetector {
    protected readonly probEMA = new RunningEMA(0.5, 5); // 32ms*5 ~ 150ms
    protected readonly longProbEMA = new RunningEMA(0.5, 64); // 32ms*64 ~ 2s
    protected readonly probMedian = new RunningUnitMedian();
    protected readonly minSpeechSamples: number;
    protected readonly maxSpeechSamples: number;
    protected readonly maxPauseCancelSamples: number;
    // private readonly results =  new Array<number>();

    protected sampleCount = 0;
    protected pauseOffset?: number = null;
    protected pauseCancelSamples: number = 0;
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
        this.resetInternal && this.resetInternal();
    }

    public async appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | number> {
        let currentEvent = this.lastActivityEvent;
        const gain = approximateGain(monoPcm);
        // debugLog?.log('appendChunk:', currentEvent, gain);
        if (gain < 0.0015 && currentEvent.kind === 'end')
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
        const pauseProbTrigger = 0.15 * probMedian;

        if (currentEvent.kind === 'end'
            && probEma >= longProbEma
            && (probEma >= speechProbTrigger)
        ) {
            // speech start detected
            const offset = Math.max(0, currentOffset - monoPcm.length);
            const duration = offset - currentEvent.offset;
            currentEvent = { kind: 'start', offset, speechProb: probEma, duration };
            this.whenTalkingProbMedian = new RunningUnitMedian();
            this.maxPauseSamples = this.sampleRate * AV.MAX_PAUSE;
        }
        else if (currentEvent.kind === 'start' && probEma < longProbEma && probEma < pauseProbTrigger) {
            // pause start detected
            this.pauseCancelSamples = 0;
            if (this.pauseOffset === null)
                this.pauseOffset = currentOffset;
            this.whenTalkingProbMedian = null;
        }

        if (currentEvent.kind === 'start') {
            const currentSpeechSamples = currentOffset - currentEvent.offset;

            if (this.pauseOffset !== null) {
                // we detected pause earlier - should we "materialize" it?
                if (probEma >= speechProbTrigger) {
                    // and it's speech now
                    this.pauseCancelSamples += monoPcm.length;
                    if (this.pauseCancelSamples >= this.maxPauseCancelSamples) {
                        // which continues for
                        this.pauseOffset = null;
                        this.pauseCancelSamples = 0;
                    }
                } else if (probEma < pauseProbTrigger) {
                    // it's still a pause
                    const currentSilenceSamples = currentOffset - this.pauseOffset;
                    if (currentSilenceSamples > this.maxPauseSamples && currentSpeechSamples > this.minSpeechSamples) {
                        // "materializing" the pause
                        const offset = this.pauseOffset + monoPcm.length;
                        const duration = offset - currentEvent.offset;
                        currentEvent = { kind: 'end', offset, speechProb: probEma, duration };
                        this.pauseOffset = null;
                    }
                }
            }
            else if (this.whenTalkingProbMedian !== null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                const offset = currentOffset + monoPcm.length;
                const duration = offset - currentEvent.offset;
                const durationS = duration / this.sampleRate;
                const speechRatio = this.whenTalkingProbMedian.sampleCount / duration;
                if (speechRatio > 0.5 && durationS > 2)
                    this.probMedian.appendSample(probEma);
            }

            if (currentEvent.kind === 'start' && currentSpeechSamples > this.maxSpeechSamples) {
                // break long speech regardless of speech probability
                const offset = this.pauseOffset ?? currentOffset;
                const duration = offset - currentEvent.offset;
                currentEvent = { kind: 'end', offset: currentOffset, speechProb: probEma, duration };
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

// WebRtcVoiceActivityDetector

enum VadActivity {
    Silence = 0,
    Voice = 1,
    Error = -1,
}

export class WebRtcVoiceActivityDetector extends VoiceActivityDetectorBase {
    constructor(private baseVad: WebRtcVad) {
        super(false, AR.SAMPLE_RATE);
    }

    public override init(): Promise<void> {
        // @ts-ignore
        return Promise.resolve(undefined);
    }

    protected override appendChunkInternal(monoPcm: Float32Array): Promise<number | null> {
        if (monoPcm.length !== AR.SAMPLES_PER_WINDOW_30)
            throw new Error(`appendChunk() accepts ${AR.SAMPLES_PER_WINDOW_30} sample audio windows only.`);

        const activity = this.baseVad.detect(monoPcm.buffer);
        if (activity == VadActivity.Error)
            throw new Error(`Error calling WebRtc VAD`);

        // Our base class logic has been developed for float speech probability about 0.75 and higher,
        // so let's adjust 1|0 to tested range to reuse existing heuristics
        return Promise.resolve(Number(0.8 * activity));
    }

    protected override resetInternal() {
        this.baseVad.reset();
    }
}

// NNVoiceActivityDetector

export class NeuralVoiceActivityDetector extends VoiceActivityDetectorBase {
    private readonly modelUri: URL;

    private readonly context: Float32Array;
    private readonly buffer: Float32Array;
    private session: ort.InferenceSession = null;
    private state: ort.Tensor;

    constructor(modelUri: URL, lastActivityEvent: VoiceActivityChange) {
        super(true, AR.SAMPLE_RATE, lastActivityEvent);

        this.modelUri = modelUri;
        this.context = new Float32Array(AV.NN_VAD_CONTEXT_SAMPLES).fill(0);
        this.buffer = new Float32Array(AR.SAMPLES_PER_WINDOW_32 + AV.NN_VAD_CONTEXT_SAMPLES).fill(0);
        this.resetInternal();

        // Multithreading requires Cross Origin Isolation, so we don't use it here. See:
        // - https://web.dev/articles/cross-origin-isolation-guide
        // ort.env.wasm.numThreads = 4;
        ort.env.wasm.numThreads = 1;
        ort.env.wasm.simd = true;
        ort.env.wasm.wasmPaths = '/dist/wasm/';
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
        const { state, buffer, context } = this;
        if (this.session == null) {
            // skip processing until initialized
            return null;
        }

        if (monoPcm.length !== AR.SAMPLES_PER_WINDOW_32) {
            throw new Error(`appendChunk() accepts ${AR.SAMPLES_PER_WINDOW_32} sample audio windows only.`);
        }

        buffer.set(context);
        buffer.set(monoPcm, context.length);
        const tensor = new ort.Tensor(buffer, [1, buffer.length]);
        const srArray = new BigInt64Array(1).fill(BigInt(16000));
        const sr = new ort.Tensor(srArray);
        const feeds = { input: tensor, state: state, sr: sr };
        const result = await this.session.run(feeds);
        const { output, stateN } = result;
        this.state = stateN;
        this.context.set(monoPcm.slice(-context.length));
        return output.data[0] as number;
    }

    protected resetInternal(): void {
        this.state = new ort.Tensor(new Float32Array(2 * 128), [2, 1, 128]);
    }
}

// Helpers

function adjustChangeEventsToSeconds(event: VoiceActivityChange, sampleRate: number): VoiceActivityChange {
    return {
        kind: event.kind,
        offset: event.offset / sampleRate,
        speechProb: event.speechProb,
        duration: event.duration === null ? null : event.duration / sampleRate
    };
}
