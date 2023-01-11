import Denque from 'denque';
import {
    GetStateNodeMessage,
    InitNodeMessage,
    NodeMessage,
    OperationCompletedProcessorMessage,
    ProcessorState,
    StateChangedProcessorMessage,
    StateProcessorMessage,
} from './feeder-audio-worklet-message';
import {
    DecoderWorkerMessage,
    EndDecoderWorkerMessage,
    SamplesDecoderWorkerMessage,
} from '../workers/opus-decoder-worker-message';
import { Log, LogLevel } from 'logging';

const LogScope = 'FeederProcessor';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const SAMPLE_RATE = 48000;

/** Part of the feeder that lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor {
    private readonly chunks = new Denque<Float32Array | 'end'>();
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 240_000 samples at 48 kHz ~= 5_000 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private readonly samplesLowThreshold: number = 480_000;
    /**
     * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
     * should be in sync with audio-feeder bufferSize
     */
    private readonly enoughToStartPlaying: number = 0.1;
    /** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
    private readonly tooMuchBuffered: number = 15.0;
    private workerPort: MessagePort;
    private chunkOffset = 0;
    /** In seconds from the start of playing, excluding starving time and processing time */
    private playbackTime = 0;
    private isPlaying = false;
    private isPaused = false;
    private isStarving = false;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.port.onmessage = this.onNodeMessage;
    }

    /** Count how many samples are queued up */
    private get bufferedSampleCount(): number {
        const { chunks, chunkOffset } = this;
        let result = -chunkOffset;
        const len = chunks.length;
        for (let i = 0; i < len; ++i) {
            const chunk = chunks.peekAt(i);
            result += chunk.length;
        }
        return result;
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        _parameters: { [name: string]: Float32Array; }
    ): boolean {
        const { chunks, isPlaying, isPaused, samplesLowThreshold, tooMuchBuffered } = this;
        let { chunkOffset } = this;
        if (outputs == null || outputs.length === 0 || outputs[0].length === 0) {
            return true;
        }

        const output = outputs[0];
        // we only support mono output at the moment
        const channel = output[0];
        warnLog?.assert(channel.length === 128, `process: WebAudio's render quantum size must be 128`);

        if (!isPlaying || isPaused) {
            // write silence, because we don't playing
            channel.fill(0);
            return true;
        }

        for (let offset = 0; offset < channel.length;) {
            const chunk = chunks.peekFront();
            if (chunk !== undefined) {
                if (chunk === 'end') {
                    channel.fill(0, offset);
                    debugLog?.log(`process: reached end of stream`);
                    this.onStopMessage();
                    break;
                }
                else {
                    const chunkAvailable = chunk.length - chunkOffset;
                    const remaining = channel.length - offset;

                    if (chunkAvailable >= remaining) {
                        const remainingSamples = chunk.subarray(chunkOffset, chunkOffset + remaining);
                        channel.set(remainingSamples, offset);
                        chunkOffset += remaining;
                        offset += remaining;
                        this.playbackTime += remaining / SAMPLE_RATE;
                    }
                    else {
                        const remainingSamples = chunk.subarray(chunkOffset);
                        channel.set(remainingSamples, offset);
                        offset += remainingSamples.length;
                        this.playbackTime += remainingSamples.length / SAMPLE_RATE;

                        chunkOffset = 0;
                        chunks.shift();
                    }
                }
            }
            // we don't have enough data to continue playing => starving
            else {
                channel.fill(0, offset);
                if (!this.isStarving) {
                    this.isStarving = true;
                    this.postStateChangedMessage('starving');
                }

                break;
            }
        }
        this.chunkOffset = chunkOffset;
        const sampleCount = this.bufferedSampleCount;
        const bufferedDuration = sampleCount / SAMPLE_RATE;
        if (this.isPlaying && !this.isStarving) {
            if (sampleCount <= samplesLowThreshold) {
                this.postStateChangedMessage('playingWithLowBuffer');
            }
            if (bufferedDuration > tooMuchBuffered) {
                this.postStateChangedMessage('playingWithTooMuchBuffer');
            }
        } else if (this.isStarving) {
            if (sampleCount > samplesLowThreshold) {
                this.isStarving = false;

                if (this.isPlaying) {
                    this.postStateChangedMessage('playing');
                }
            }
        }
        return true;
    }

    private onNodeMessage = (ev: MessageEvent<NodeMessage>): void => {
        const msg = ev.data;
        switch (msg.type) {
        case 'init':
            this.onInitMessage(msg as InitNodeMessage);
            break;
        case 'stop':
            this.onStopMessage();
            break;
        case 'pause':
            this.onPauseMessage(true);
            break;
        case 'resume':
            this.onPauseMessage(false);
            break;
        case 'getState':
            this.onGetState(msg as GetStateNodeMessage);
            break;

        default:
            throw new Error(`Unsupported message type: ${msg.type}`);
        }
    };

    private onInitMessage(message: InitNodeMessage) {
        this.workerPort = message.decoderWorkerPort;
        this.workerPort.onmessage = this.onWorkerMessage;
        const msg: OperationCompletedProcessorMessage = {
            type: 'operationCompleted',
            callbackId: message.callbackId,
        };
        this.port.postMessage(msg);
    }

    private reset(): void {
        debugLog?.log(`reset`);
        this.isPlaying = false;
        this.isPaused = false;
        this.isStarving = false;
        this.chunks.clear();
        this.chunkOffset = 0;
        // we don't set playbackTime = 0, because we want to see it in getState() after a stop.
    }

    private onGetState(message: GetStateNodeMessage) {
        const msg: StateProcessorMessage = {
            type: 'state',
            callbackId: message.callbackId,
            bufferedTime: this.bufferedSampleCount / SAMPLE_RATE,
            playbackTime: this.playbackTime,
        };
        debugLog?.log(`onGetState, message:`, msg);
        this.port.postMessage(msg);
    }

    private onStopMessage() {
        const { isPlaying: wasPlaying } = this;
        this.reset();

        if (wasPlaying) {
            debugLog?.log(`onStopMessage`);
            this.postStateChangedMessage('stopped');
        }
        this.postStateChangedMessage('ended');
    }

    private onPauseMessage(isPause: boolean) {
        if (this.isPaused === isPause) {
            debugLog?.log(`onPauseMessage: already in pause state:`, this.isPaused);
            return;
        }
        this.isPaused = isPause;
        if (isPause) {
            this.postStateChangedMessage('paused');
        }
        else {
            this.postStateChangedMessage('resumed');
        }
    }

    private startPlaybackIfEnoughBuffered(): void {
        if (!this.isPlaying) {
            const bufferedDuration = this.bufferedSampleCount / SAMPLE_RATE;
            if (bufferedDuration >= this.enoughToStartPlaying) {
                this.isPlaying = true;
                this.playbackTime = 0;
                const message: StateChangedProcessorMessage = {
                    type: 'stateChanged',
                    state: 'playing',
                };
                this.port.postMessage(message);
            }
        }
    }

    private onWorkerMessage = (ev: MessageEvent<DecoderWorkerMessage>): void => {
        const msg = ev.data;
        try {
            switch (msg.type) {
            case 'samples':
                this.onSamplesDecoderWorkerMessage(msg as SamplesDecoderWorkerMessage);
                break;
            case 'end':
                this.onEndDecoderWorkerMessage(msg as EndDecoderWorkerMessage);
                break;
            default:
                throw new Error(`Unsupported message type: ${msg.type}`);
            }
        }
        catch (error) {
            errorLog?.log(`onWorkerMessage: unhandled error:`, error);
        }
    };

    private onEndDecoderWorkerMessage(message: EndDecoderWorkerMessage) {
        this.chunks.push('end');
        // if we don't start to play and the 'end' is already here
        // for example if play threshold > number of frames before the end
        if (!this.isPlaying) {
            this.reset();
            this.postStateChangedMessage('ended');
        }
    }

    private onSamplesDecoderWorkerMessage(message: SamplesDecoderWorkerMessage) {
        const { buffer, length, offset } = message;
        this.chunks.push(new Float32Array(buffer.slice(offset, offset + length)));
        this.startPlaybackIfEnoughBuffered();
    }

    private postStateChangedMessage(state: ProcessorState)
    {
        const message: StateChangedProcessorMessage = {
            type: 'stateChanged',
            state: state,
        };
        this.port.postMessage(message);
    }
}

// @ts-expect-error - register  is defined
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('feederWorklet', FeederAudioWorkletProcessor);
