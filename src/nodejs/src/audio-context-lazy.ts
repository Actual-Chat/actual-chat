const LogScope = 'AudioContextLazy';

export class AudioContextLazy {
    private whenInitialized?: Promise<void> = null;
    private audioContext?: AudioContext = null;

    constructor() {
        this.addInitEventListeners();
    }

    public async get(): Promise<AudioContext> {
        if (!this.audioContext) {
            await this.whenInitialized;
        }

        await AudioContextLazy.resume(this.audioContext);

        return this.audioContext;
    }

    private async initialize(): Promise<void> {
        const audioContext = await AudioContextLazy.create();
        await this.warmup(audioContext);
        this.audioContext = audioContext;
    }

    private static async create() {
        const audioContext = new AudioContext({
            latencyHint: 'interactive',
            sampleRate: 48000,
        });

        await AudioContextLazy.resume(audioContext);

        await Promise.all([
            audioContext.audioWorklet.addModule('/dist/feederWorklet.js'),
            audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js'),
            audioContext.audioWorklet.addModule('/dist/vadWorklet.js'),
        ]);
        return audioContext;
    }

    private static async resume(audioContext: AudioContext) {
        console.log(`${LogScope}.resume: audioContext.state=`, audioContext.state);
        if (audioContext.state !== 'running' && audioContext.state !== 'closed') {
            await audioContext.resume();
        }
        console.log(`${LogScope}.resume: audioContext.state=`, audioContext.state);
    }

    private async warmup(audioContext: AudioContext): Promise<void> {
        console.debug(`${LogScope}: Start warming up AudioContext`);
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
        console.debug(`${LogScope}: End of warming up AudioContext`);
    }

    private addInitEventListeners() {
        self.addEventListener('click', this.onUserEvent, {once: true});
        self.addEventListener('doubleclick', this.onUserEvent, {once: true});
        self.addEventListener('onkeydown', this.onUserEvent, {once: true});
        self.addEventListener('touchend', this.onUserEvent, {once: true});
    }

    private onUserEvent = (): void => {
        if (this.whenInitialized === null) {
            this.whenInitialized = this.initialize().then();
        }
    }
}

export const audioContextLazy = new AudioContextLazy();
