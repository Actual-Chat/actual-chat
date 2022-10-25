import { Log, LogLevel } from 'logging';

const LogScope = 'WarmUpAudioWorkletProcessor';
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

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

// @ts-ignore
// eslint-disable-next-line @typescript-eslint/no-unsafe-call
registerProcessor('warmUpWorklet', WarmUpAudioWorkletProcessor);
