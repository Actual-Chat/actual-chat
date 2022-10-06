import { addInteractionHandler } from 'first-interaction';
import { Disposable } from 'disposable';
import { delayAsync } from 'promises';

const LogScope = 'AudioContextLazy';

async function defaultFactory() : Promise<AudioContext> {
    const audioContext = new AudioContext({
        latencyHint: 'interactive',
        sampleRate: 48000,
    });

    // Resume must be called during the sync part of this async flow,
    // i.e. it must be the very first async call
    await resume(audioContext);

    await Promise.all([
        audioContext.audioWorklet.addModule('/dist/feederWorklet.js'),
        audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js'),
        audioContext.audioWorklet.addModule('/dist/vadWorklet.js'),
    ]);

    await warmup(audioContext);

    return audioContext;
}

async function resume(audioContext: AudioContext) : Promise<AudioContext> {
    console.debug(`${LogScope}.resume start: audioContext.state =`, audioContext.state);
    if (audioContext.state !== 'running' && audioContext.state !== 'closed') {
        const resumeTask = audioContext.resume().then(() => true);
        const delayTask = delayAsync(250).then(() => false);
        const result = await Promise.race([resumeTask, delayTask]);
        if (!result)
            throw `${LogScope}: Couldn't resume AudioContext.`;
    }

    console.debug(`${LogScope}.resume end: audioContext.state =`, audioContext.state);
    return audioContext;
}

async function warmup(audioContext: AudioContext): Promise<AudioContext> {
    console.debug(`${LogScope}.warmup: starting...`);

    await audioContext.audioWorklet.addModule('/dist/warmUpWorklet.js');
    const nodeOptions: AudioWorkletNodeOptions = {
        channelCount: 1,
        channelCountMode: 'explicit',
        numberOfInputs: 0,
        numberOfOutputs: 1,
        outputChannelCount: [1],
    };
    const node = new AudioWorkletNode(audioContext, 'warmUpWorklet', nodeOptions);
    node.connect(audioContext.destination);
    await new Promise<void>(resolve => {
        node.port.postMessage('stop');
        node.port.onmessage = (ev: MessageEvent<string>): void => {
            console.assert(ev.data === 'stopped', 'Unsupported message from warm up worklet.');
            resolve();
        };
    });
    node.disconnect();
    node.port.onmessage = null;
    node.port.close();

    console.debug(`${LogScope}.warmup: done`);
    return audioContext;
}

export class AudioContextLazy implements Disposable {
    private readonly audioContextTask: Promise<AudioContext> = null;
    private firstInteractionDisposable?: Disposable = null;

    constructor(factory: (() => Promise<AudioContext>) = null) {
        factory ??= defaultFactory;
        this.audioContextTask = new Promise<AudioContext>(resolve => {
            this.firstInteractionDisposable = addInteractionHandler(LogScope, async () => {
                const audioContext = await factory();
                resolve(audioContext);
                return true;
            });
        })
    }

    public dispose(): void {
        this.firstInteractionDisposable?.dispose();
    }

    public get(): Promise<AudioContext> {
        return this.audioContextTask;
    }
}

export const audioContextLazy = new AudioContextLazy();
