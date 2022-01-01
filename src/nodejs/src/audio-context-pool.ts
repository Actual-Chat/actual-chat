/**
 * We're only allowed to have 4-6 audio contexts on many browsers
 * and there's no way to discard them before GC, so we should reuse audio contexts.
 */
// TODO: make the pooling with node objects too
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
/** helper function with workaround of Safari addModule implementation */
async function getWorklet(url: string): Promise<string> {
    if (self.navigator.userAgent.includes('Safari') && !self.navigator.userAgent.includes('Chrome')) {
        const response = await fetch(url);
        const text = await response.text();
        return text;
    } else {
        return url;
    }
}

AudioContextPool.register("main", async () => {
    const audioContext = new AudioContext({
        latencyHint: 'interactive',
        sampleRate: 48000,
    });
    const feederWorklet = await getWorklet('/dist/feederWorklet.js');
    await audioContext.audioWorklet.addModule(feederWorklet);
    const opusEncoderWorklet = await getWorklet('/dist/opusEncoderWorklet.js');
    await audioContext.audioWorklet.addModule(opusEncoderWorklet);
    return audioContext;
});
// TODO: try to use OfflineAudioContext for VAD
AudioContextPool.register("vad", async () => {
    const audioContext = new AudioContext({
        latencyHint: 'interactive',
        sampleRate: 16000,
    });
    const vadWorklet = await getWorklet('/dist/vadWorklet.js');
    await audioContext.audioWorklet.addModule(vadWorklet);
    // warmup the audioContext nodes
    const audioWorkletOptions: AudioWorkletNodeOptions = {
        numberOfInputs: 1,
        numberOfOutputs: 1,
        channelCount: 1,
        channelInterpretation: 'speakers',
        channelCountMode: 'explicit',
    };
    const vadWorkletNode = new AudioWorkletNode(audioContext, 'audio-vad-worklet-processor', audioWorkletOptions);
    vadWorkletNode.connect(audioContext.destination);
    vadWorkletNode.disconnect();
    return audioContext;
});

AudioContextPool.init();
