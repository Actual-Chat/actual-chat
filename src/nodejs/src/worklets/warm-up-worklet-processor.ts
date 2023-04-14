import { Log } from 'logging';
import { timerQueue } from 'timerQueue';

const { warnLog } = Log.get('WarmUpAudioWorkletProcessor');
/* eslint-disable @typescript-eslint/ban-ts-comment */
/**
 * Produces silence. We use the worklet to warm up a browser's audio pipeline.
 * Lives in [AudioWorkletGlobalScope]{@link https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletGlobalScope} */
class WarmUpAudioWorkletProcessor extends AudioWorkletProcessor {
    private isStopped = false;
    private wroteSilence = false;

    constructor(options: AudioWorkletNodeOptions) {
        super(options);
        this.port.onmessage = this.onNodeMessage;
    }

    public process(
        _inputs: Float32Array[][],
        outputs: Float32Array[][],
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
        _parameters: { [name: string]: Float32Array; }
    ): boolean {
        timerQueue?.triggerExpired();
        // we should write silence at least once
        if (this.isStopped && this.wroteSilence) {
            this.port.postMessage('stopped');
            return false;
        }

        if (outputs == null || outputs.length === 0 || outputs[0].length === 0) {
            return true;
        }
        const output = outputs[0];
        const channel = output[0];

        // write silence
        channel.fill(0);
        this.wroteSilence = true;
        return true;
    }

    private onNodeMessage = (ev: MessageEvent<string>): void => {
        const msg = ev.data;
        warnLog?.assert(msg === 'stop', `WarmUpAudioWorkletProcessor: Unsupported message: ${msg}`);
        this.isStopped = true;
    };
}

registerProcessor('warmUpWorklet', WarmUpAudioWorkletProcessor);
