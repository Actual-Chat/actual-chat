import { Disposable } from 'disposable';
import { delayAsync, PromiseSource } from 'promises';
import { onDeviceAwake } from 'on-device-awake';
import { EventHandlerSet } from 'event-handling';
import { NextInteraction } from 'next-interaction';
import { Log, LogLevel } from 'logging';

const LogScope = 'AudioContextLazy';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const errorLog = Log.get(LogScope, LogLevel.Error);

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
    debugLog?.log(`-> resume: audioContext.state =`, audioContext.state);
    if (force || audioContext.state !== 'running' && audioContext.state !== 'closed') {
        if (force) {
            await audioContext.suspend();
        }
        const resumeTask = audioContext.resume().then(() => true);
        const delayTask = delayAsync(250).then(() => false);
        const result = await Promise.race([resumeTask, delayTask]);
        if (!result)
            throw new Error(`${LogScope}: Couldn't resume AudioContext.`);
    }

    debugLog?.log(`<- resume: audioContext.state =`, audioContext.state);
    return audioContext;
}

async function warmup(audioContext: AudioContext): Promise<AudioContext> {
    debugLog?.log(`-> warmup`);

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
            warnLog?.assert(ev.data === 'stopped', 'Unsupported message from warm up worklet.');
            resolve();
        };
    });
    node.disconnect();
    node.port.onmessage = null;
    node.port.close();

    debugLog?.log(`<- warmup`);
    return audioContext;
}

export class AudioContextLazy implements Disposable {
    private audioContextTask?: PromiseSource<AudioContext> = null;
    private nextInteractionHandler?: Disposable = null;
    private requireInteraction = true;

    public audioContext: AudioContext | null = null;
    public audioContextChanged: EventHandlerSet<AudioContext | null> = new EventHandlerSet<AudioContext | null>();

    constructor() {
        this.audioContextTask = new PromiseSource<AudioContext>();
        if (this.requireInteraction) {
            this.nextInteractionHandler = NextInteraction.addHandler(async () => {
                const audioContext = await defaultFactory();
                this.setAudioContext(audioContext);
                onDeviceAwake(() => this.wakeUp());
            });
        }
        else {
            void defaultFactory()
                .then(audioContext => this.setAudioContext(audioContext))
                .then(() => onDeviceAwake(() => this.wakeUp()));
        }
    }

    public dispose(): void {
        this.nextInteractionHandler?.dispose();
    }

    public async get(): Promise<AudioContext> {
        const audioContext = this.audioContext;
        if (audioContext) {
            let lastContextTime = audioContext['lastTime'] as number;
            let currentContextTime = audioContext.currentTime;
            if (lastContextTime == currentContextTime) {
                if (audioContext.state === 'suspended')
                    return resume(audioContext);

                // The context might stuck after wake up
                await delayAsync(20);
                lastContextTime = audioContext['lastTime'] as number;
                currentContextTime = audioContext.currentTime;
                if (lastContextTime == currentContextTime) {
                    // We can't resume it - user gesture context has already been lost
                    this.refreshAudioContextTask();
                    return this.audioContextTask;
                }
            }
            else {
                audioContext['lastTime'] = audioContext.currentTime;
                // The context might stuck after wake up
                await delayAsync(20);
                lastContextTime = audioContext['lastTime'] as number;
                currentContextTime = audioContext.currentTime;
                if (lastContextTime == currentContextTime) {
                    // We can't resume it - user gesture context has already been lost
                    this.refreshAudioContextTask();
                    return this.audioContextTask;
                }
            }
            return audioContext;
        }
        return this.audioContextTask;
    }

    public doNotWaitForInteraction(): void {
        const audioContext = this.audioContext;
        this.requireInteraction = false;
        this.nextInteractionHandler?.dispose();
        this.nextInteractionHandler = null;
        if (audioContext) {
            this.setAudioContext(audioContext);
        }
        else {
            void defaultFactory()
                .then(audioContext1 => this.setAudioContext(audioContext1));
        }
    }

    private setAudioContext(audioContext: AudioContext): void {
        audioContext['lastTime'] = audioContext.currentTime;
        this.audioContext = audioContext;
        this.audioContextTask.resolve(audioContext);
        this.audioContextChanged.triggerSilently(audioContext);
    }

    private refreshAudioContextTask(): void {
        const audioContext = this.audioContext;
        this.nextInteractionHandler?.dispose();
        this.nextInteractionHandler = null;
        this.audioContext = null;
        this.audioContextTask = new PromiseSource<AudioContext>();
        this.audioContextChanged.triggerSilently(null);
        if (this.requireInteraction) {
            this.nextInteractionHandler = NextInteraction.addHandler(() => resume(audioContext, true)
                .then(audioContext1 => this.setAudioContext(audioContext1)));
        } else {
            void resume(audioContext, true)
                .then(audioContext1 => this.setAudioContext(audioContext1));
        }
    }

    private wakeUp(): void {
        if (this.audioContext === null)
            return;

        this.refreshAudioContextTask();
    }
}

export const audioContextLazy = new AudioContextLazy();
