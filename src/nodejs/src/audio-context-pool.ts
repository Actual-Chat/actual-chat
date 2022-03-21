import { AudioRecorder } from '../../dotnet/Audio.UI.Blazor/Components/AudioRecorder/audio-recorder';

/**
 * We're only allowed to have 4-6 audio contexts on many browsers
 * and there's no way to discard them before GC, so we should reuse audio contexts.
 */
export class AudioContextPool {

    private static audioContexts = new Map<string, {
        audioContext: BaseAudioContext | null,
        factory: () => Promise<BaseAudioContext>,
    }>();

    public static register(key: string, factory: () => Promise<BaseAudioContext>): void {
        if (AudioContextPool.audioContexts.has(key))
            throw new Error(`AudioContext with key "${key}" is already registered.`);
        AudioContextPool.audioContexts.set(key, { audioContext: null, factory: factory });
    }

    private static initPromise?: Promise<void> = null;

    /**
     * Use the function as close as possibly to the start of work,
     * not in constructor, because for creation of an audio context we should got an user gesture action.
     * Don't close the context, because it's shared across the app.
     */
    public static async get(key: string): Promise<BaseAudioContext> {
        if (this.initPromise !== null) {
            await this.initPromise;
            this.initPromise = null;
        }
        const obj = AudioContextPool.audioContexts.get(key);
        if (obj === undefined)
            throw new Error(`AudioContext factory with key "${key}" isn't registered.`);

        if (obj.audioContext === null) {
            console.warn(`AudioContextPool get(): audioContext '${key}' wasn't initialized`);
            obj.audioContext = await obj.factory();
        }
        if (!isAudioContext(obj.audioContext)) {
            console.error(`AudioContextPool: not an AudioContext:`, obj.audioContext);
            return obj.audioContext;
        }
        if (obj.audioContext.state === 'suspended') {
            console.warn(`AudioContextPool get(): trying to  resume, '${key}' -> ${JSON.stringify(obj.audioContext)}`);
            await obj.audioContext.resume();
            console.log(`AudioContextPool get(): resumed, '${key}' -> ${JSON.stringify(obj.audioContext)}`);
        }
        return obj.audioContext;
    }

    /**
     * Helps to decrease initialization latency by creation the audio contexts as soon as we could
     * after user interaction.
     */
    public static init() {
        self.addEventListener('touchstart', AudioContextPool._initEventListener);
        self.addEventListener('onkeydown', AudioContextPool._initEventListener);
        self.addEventListener('mousedown', AudioContextPool._initEventListener);
        self.addEventListener('pointerdown', AudioContextPool._initEventListener);
        self.addEventListener('pointerup', AudioContextPool._initEventListener);
    }

    private static removeInitListeners() {
        self.removeEventListener('touchstart', AudioContextPool._initEventListener);
        self.removeEventListener('onkeydown', AudioContextPool._initEventListener);
        self.removeEventListener('mousedown', AudioContextPool._initEventListener);
        self.removeEventListener('pointerdown', AudioContextPool._initEventListener);
        self.removeEventListener('pointerup', AudioContextPool._initEventListener);
    }

    private static _initEventListener = (): void => {

        const initializeMapItem = async (obj: {
            audioContext: BaseAudioContext | null,
            factory: () => Promise<BaseAudioContext>,
        }, key: string): Promise<void> => {
            if (obj.audioContext != null)
                return;
            obj.audioContext = await obj.factory();
            console.debug(`AudioContextPool: AudioContext "${key}" is created.`);

            // Try to warm-up context
            if (isAudioContext(obj.audioContext) && obj.audioContext.state === 'running') {
                console.debug(`AudioContextPool: Start warming up AudioContext "${key}"`);
                await obj.audioContext.audioWorklet.addModule('/dist/warmUpWorklet.js');
                const nodeOptions: AudioWorkletNodeOptions = {
                    channelCount: 1,
                    channelCountMode: 'explicit',
                    numberOfInputs: 0,
                    numberOfOutputs: 1,
                    outputChannelCount: [1],
                };
                const node = new AudioWorkletNode(obj.audioContext, 'warmUpWorklet', nodeOptions);
                node.connect(obj.audioContext.destination);
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
                console.debug(`AudioContextPool: End of warming up AudioContext "${key}"`);
            } else {
                console.debug(`AudioContextPool: Can't warm up AudioContext: ${JSON.stringify(obj.audioContext)}`);
            }
            console.debug(`AudioContextPool: AudioContext "${key}" is initialized.`);
        };

        const initialize = async (): Promise<void> => {
            try {
                // init first recorder
                // TODO: create an application initializer and do not mix up listening and recording like this
                void AudioRecorder.initRecorderPool();
                AudioContextPool.removeInitListeners();
                const promises: Promise<void>[] = [];

                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                AudioContextPool.audioContexts.forEach((obj, key) => promises.push(initializeMapItem(obj, key)));
                await Promise.all(promises);
            }
            catch (error) {
                console.error(`Can't initialize audio contexts: ${JSON.stringify(error)}`);
            }
            finally{
                this.initPromise = null;
            }
        };
        this.initPromise = initialize();
    };
}





export function isAudioContext(obj: BaseAudioContext | AudioContext): obj is AudioContext {
    return !!obj && typeof obj === 'object' && typeof obj['resume'] === 'function';
}

AudioContextPool.register('main', async () => {
    const audioContext = new AudioContext({
        latencyHint: 'interactive',
        sampleRate: 48000,
    });
    if (audioContext.state === 'suspended')
        await audioContext.resume();
    await Promise.all([
        audioContext.audioWorklet.addModule('/dist/feederWorklet.js'),
        audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js'),
        audioContext.audioWorklet.addModule('/dist/vadWorklet.js'),
    ]);
    return audioContext;
});

AudioContextPool.init();

/// #if DEBUG
self['AudioContextPool'] = AudioContextPool;
/// #endif