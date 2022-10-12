import { addInteractionHandler } from 'first-interaction';
import { Disposable } from 'disposable';
import { delayAsync } from 'promises';
import { onDeviceAwake } from 'on-device-awake';

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

async function resume(audioContext: AudioContext, force = false) : Promise<AudioContext> {
    console.debug(`${LogScope}.resume start: audioContext.state =`, audioContext.state);
    if (force || audioContext.state !== 'running' && audioContext.state !== 'closed') {
        if (force) {
            await audioContext.suspend();
        }
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
    private audioContextTask: Promise<AudioContext> = null;
    private audioContext: AudioContext | null = null;
    private firstInteractionDisposable?: Disposable = null;

    constructor(factory: (() => Promise<AudioContext>) = null) {
        factory ??= defaultFactory;
        this.audioContextTask = new Promise<AudioContext>(resolve => {
            this.firstInteractionDisposable = addInteractionHandler(LogScope, async () => {
                const audioContext = await factory();
                audioContext['lastTime'] = audioContext.currentTime;
                onDeviceAwake(() => this.wakeUp());
                this.audioContext = audioContext;
                resolve(audioContext);
                return false;
            });
        });
    }

    public dispose(): void {
        this.firstInteractionDisposable?.dispose();
    }

    public async get(): Promise<AudioContext> {
        const audioContext = this.audioContext;
        if (audioContext) {
            let lastContextTime = audioContext['lastTime'] as number;
            let currentContextTime = audioContext.currentTime;
            if (lastContextTime == currentContextTime) {
                if (audioContext.state === 'suspended')
                    return resume(audioContext);

                // probably the context is stuck after wakeup
                await delayAsync(20);
                lastContextTime = audioContext['lastTime'] as number;
                currentContextTime = audioContext.currentTime;
                if (lastContextTime == currentContextTime) {
                    // can't do resume there - user gesture context has already been lost
                    // return resume(audioContext, true);
                    this.refreshAudioContextTask();
                    return this.audioContextTask;
                }
            }
            return audioContext;
        }
        return this.audioContextTask;
    }

    private refreshAudioContextTask() {
        const audioContext = this.audioContext;
        this.firstInteractionDisposable?.dispose();
        this.firstInteractionDisposable = null;
        this.audioContext = null;
        this.audioContextTask = new Promise<AudioContext>(resolve => {
            this.firstInteractionDisposable = addInteractionHandler(LogScope, async () => {
                await resume(audioContext, true);
                audioContext['lastTime'] = audioContext.currentTime;
                this.audioContext = audioContext;
                resolve(audioContext);
                return false;
            });
        });
    }

    private wakeUp(): void {
        if (this.audioContext === null)
            return;

        this.refreshAudioContextTask();
    }
}

export const audioContextLazy = new AudioContextLazy();
