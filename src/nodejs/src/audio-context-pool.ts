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
        AudioContextPool.audioContexts.set(key, { audioContext: null, factory: factory, });
    }

    /**
     * Use the function as close as possibly to the start of work,
     * not in constructor, because for creation of an audio context we should got an user gesture action.
     * Don't close the context, because it's shared across the app.
     */
    public static async get(key: string): Promise<BaseAudioContext> {
        let obj = AudioContextPool.audioContexts.get(key);
        if (obj === undefined)
            throw new Error(`AudioContext factory with key "${key}" isn't registered.`);

        if (obj.audioContext === null) {
            console.warn("pool get: obj.audioContext", obj.audioContext);
            obj.audioContext = await obj.factory();
        }

        if (obj.audioContext.state === 'suspended' && typeof obj.audioContext["resume"] === 'function')
            (obj.audioContext as AudioContext).resume();
        return obj.audioContext;
    }

    /**
     * Helps to decrease initialization latency by creation the audio contexts as soon as we could
     * after user interaction.
     */
    public static init() {
        self.addEventListener('onkeydown', AudioContextPool._initEventListener);
        self.addEventListener('mousedown', AudioContextPool._initEventListener);
        self.addEventListener('pointerdown', AudioContextPool._initEventListener);
        self.addEventListener('pointerup', AudioContextPool._initEventListener);
        self.addEventListener('touchstart', AudioContextPool._initEventListener);
    }

    private static removeInitListeners() {
        self.removeEventListener('onkeydown', AudioContextPool._initEventListener);
        self.removeEventListener('mousedown', AudioContextPool._initEventListener);
        self.removeEventListener('pointerdown', AudioContextPool._initEventListener);
        self.removeEventListener('pointerup', AudioContextPool._initEventListener);
        self.removeEventListener('touchstart', AudioContextPool._initEventListener);
    }

    private static _initEventListener = () => {
        AudioContextPool.removeInitListeners();
        AudioContextPool.audioContexts.forEach(async (obj, key) => {
            obj.audioContext = await obj.factory();
            console.debug(`AudioContext "${key}" is initialized.`);
        });
    };
}

AudioContextPool.register("main", async () => {
    const audioContext = new AudioContext({
        latencyHint: 'interactive',
        sampleRate: 48000,
    });
    await audioContext.audioWorklet.addModule('/dist/feederWorklet.js');
    return audioContext;
});
AudioContextPool.register("recorder", async () => {
    const audioContext = new AudioContext({ sampleRate: 48000 });
    await audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js');
    await audioContext.audioWorklet.addModule('/dist/vadWorklet.js');
    return audioContext;
});

AudioContextPool.init();
