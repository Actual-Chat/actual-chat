import { AudioRecorder } from '../../dotnet/Audio.UI.Blazor/Components/AudioRecorder/audio-recorder';

const LogScope = 'AudioContextPool';

/**
 * We're only allowed to have 4-6 audio contexts on many browsers
 * and there's no way to discard them before GC, so we should reuse audio contexts.
 */
export class AudioContextPool {
    private static whenInitialized?: Promise<void> = null;

    private static audioContexts = new Map<string, {
        audioContext: BaseAudioContext | null,
        factory: () => Promise<BaseAudioContext>,
    }>();

    public static register(key: string, factory: () => Promise<BaseAudioContext>): void {
        if (AudioContextPool.audioContexts.has(key))
            throw new Error(`AudioContext with key "${key}" is already registered.`);
        AudioContextPool.audioContexts.set(key, { audioContext: null, factory: factory });
    }

    /**
     * Use the function as close as possibly to the start of work,
     * not in constructor, because for creation of an audio context we should got an user gesture action.
     * Don't close the context, because it's shared across the app.
     */
    public static async get(key: string): Promise<BaseAudioContext> {
        if (this.whenInitialized !== null)
            await this.whenInitialized;

        const obj = AudioContextPool.audioContexts.get(key);
        if (obj === undefined)
            throw new Error(`AudioContext factory with key "${key}" isn't registered.`);

        if (obj.audioContext === null) {
            console.warn(`${LogScope}: get(): audioContext '${key}' wasn't initialized`);
            obj.audioContext = await obj.factory();
        }
        if (!isAudioContext(obj.audioContext)) {
            console.error(`${LogScope}: not an AudioContext:`, obj.audioContext);
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
    public static addInitEventListeners() {
        self.addEventListener('touchstart', AudioContextPool._initEventListener);
        self.addEventListener('onkeydown', AudioContextPool._initEventListener);
        self.addEventListener('mousedown', AudioContextPool._initEventListener);
        self.addEventListener('pointerdown', AudioContextPool._initEventListener);
        self.addEventListener('pointerup', AudioContextPool._initEventListener);
    }

    private static removeInitEventListeners() {
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
            console.debug(`${LogScope}: AudioContext "${key}" is created.`);

            // Try to warm-up context
            if (isAudioContext(obj.audioContext) && obj.audioContext.state === 'running') {
                console.debug(`${LogScope}: Start warming up AudioContext "${key}"`);
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
                console.debug(`${LogScope}: End of warming up AudioContext "${key}"`);
            } else {
                console.debug(`${LogScope}: Can't warm up AudioContext: ${JSON.stringify(obj.audioContext)}`);
            }
            console.debug(`${LogScope}: AudioContext "${key}" is initialized.`);
        };

        const initialize = async (): Promise<void> => {
            try {
                const promises: Promise<void>[] = [];
                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                AudioContextPool.audioContexts.forEach(
                    (obj, key) => promises.push(initializeMapItem(obj, key)));
                await Promise.all(promises);

                // TODO: create an application initializer and do not mix up
                //       listening and recording like this
                await AudioRecorder.initRecorderPool();
            }
            catch (error) {
                console.error(`Can't initialize audio contexts: ${JSON.stringify(error)}`);
            }
            finally {
                this.whenInitialized = null;
            }
        };

        this.whenInitialized = initialize();
        AudioContextPool.removeInitEventListeners();
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

AudioContextPool.addInitEventListeners();

/// #if DEBUG
self['AudioContextPool'] = AudioContextPool;
/// #endif
