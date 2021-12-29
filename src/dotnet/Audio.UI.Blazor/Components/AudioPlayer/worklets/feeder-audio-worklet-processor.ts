import Denque from 'denque';
import { NodeMessage, DataNodeMessage, ChangeStateNodeMessage, StateChangedProcessorMessage, StateProcessorMessage, GetStateNodeMessage } from './feeder-audio-worklet-message';

/** Part of the feeder that lives in AudioWorkletGlobalScope */
// TODO: implement pause
class FeederAudioWorkletProcessor extends AudioWorkletProcessor {

    private readonly debug: boolean = false;
    private readonly chunks = new Denque<Float32Array>();
    /**
     * 128 samples at 48 kHz ~= 2.67 ms
     * 480_000 samples at 48 kHz ~= 10_000 ms
     */
    private readonly samplesLowThreshold: number = 480_000;
    private chunkOffset: number = 0;
    private playStartTime: number = 0;
    private lastProcessTime: number = 0;
    private isPlaying: boolean = false;
    private isStarving: boolean = false;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.port.onmessage = this.onNodeMessage;
    }

    /** Count how many samples are queued up */
    private get sampleCount(): number {
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
        const { debug, chunks, isPlaying, samplesLowThreshold } = this;
        let { chunkOffset } = this;
        // if we are disconnected from output (node,channel) then we can be closed
        if (outputs == null || outputs.length === 0 || outputs[0].length === 0) {
            if (debug)
                console.warn('Feeder processor: empty outputs, stop processing');
            return false;
        }
        const output = outputs[0];
        // we only support mono output at the moment
        let channel = output[0];
        if (debug) {
            console.assert(channel.length === 128, "Feeder processor: WebAudio's render quantum size must be 128");
        }
        if (!isPlaying) {
            // write silence, because we don't playing
            channel.fill(0);
            return true;
        }
        if (this.playStartTime === 0) {
            this.playStartTime = new Date().getTime();
            this.lastProcessTime = this.playStartTime;
        }
        for (let offset = 0; offset < channel.length;) {
            const chunk = chunks.peekFront();
            if (chunk !== undefined) {
                if (this.isStarving) {
                    this.isStarving = false;
                    const message: StateChangedProcessorMessage = {
                        type: 'stateChanged',
                        state: 'playing',
                    };
                    this.port.postMessage(message);
                }

                this.isStarving = false;
                const chunkAvailable = chunk.length - chunkOffset;
                const remaining = channel.length - offset;

                if (chunkAvailable >= remaining) {
                    const remainingSamples = chunk.subarray(chunkOffset, chunkOffset + remaining);
                    channel.set(remainingSamples, offset);
                    chunkOffset += remaining;
                    offset += remaining;
                }
                else {
                    const remainingSamples = chunk.subarray(chunkOffset);
                    channel.set(remainingSamples, offset);
                    offset += remainingSamples.length;

                    chunkOffset = 0;
                    chunks.shift();
                }
            }
            // we don't have enough data to continue playing => starved
            else {
                channel.fill(0, offset);
                if (!this.isStarving) {
                    this.isStarving = true;
                    const message: StateChangedProcessorMessage = {
                        type: 'stateChanged',
                        state: 'starving',
                    };
                    this.port.postMessage(message);
                }

                break;
            }
        }
        this.chunkOffset = chunkOffset;
        if (this.isPlaying && !this.isStarving && this.sampleCount <= samplesLowThreshold) {
            const message: StateChangedProcessorMessage = {
                type: 'stateChanged',
                state: 'playingWithLowBuffer',
            };
            this.port.postMessage(message);
        }
        this.lastProcessTime = new Date().getTime();
        return true;
    }

    private onNodeMessage = (ev: MessageEvent<NodeMessage>): void => {
        const msg = ev.data;
        switch (msg.type) {
            case 'data': {
                this.onDataMessage(msg as DataNodeMessage);
                break;
            }
            case 'clear': {
                this.onClearMessage();
                break;
            }
            case 'getState': {
                this.onGetState(msg as GetStateNodeMessage);
            }
            case 'changeState': {
                this.onChangeStateMessage(msg as ChangeStateNodeMessage);
                break;
            }
            default:
                throw new Error(`Feeder processor: Unsupported message type: ${msg.type}`);
        }
    };

    private onDataMessage(message: DataNodeMessage) {
        this.chunks.push(new Float32Array(message.buffer));
    }

    private onClearMessage() {
        const { debug } = this;
        if (debug)
            console.debug("Feeder processor: clear");
        this.isPlaying = false;
        this.isStarving = false;
        this.chunks.clear();
    }

    private onGetState(message: GetStateNodeMessage) {
        const { debug } = this;
        const msg: StateProcessorMessage = {
            type: 'state',
            id: message.id,
            sampleCount: this.sampleCount,
            playbackTime: Math.max(0, this.lastProcessTime - this.playStartTime),
        };
        if (debug)
            console.debug("Feeder processor: get state", msg);
        this.port.postMessage(msg);
    }

    private onChangeStateMessage(message: ChangeStateNodeMessage) {
        const { debug } = this;
        if (this.isPlaying && message.state === "stop") {
            this.isStarving = false;
            this.isPlaying = false;
            if (debug)
                console.debug("Feeder processor: stopping");
            const message: StateChangedProcessorMessage = {
                type: 'stateChanged',
                state: 'stopped',
            };
            this.port.postMessage(message);
        }
        else if (!this.isPlaying && message.state === "play") {
            this.isStarving = true;
            this.isPlaying = true;
            this.playStartTime = 0;
            if (debug)
                console.debug("Feeder processor: start playing");
        }
    }
}

registerProcessor('feederWorklet', FeederAudioWorkletProcessor as unknown as AudioWorkletProcessorConstructor);

