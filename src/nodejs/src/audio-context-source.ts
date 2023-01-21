import { Disposable } from 'disposable';
import { delayAsync, PromiseSource, PromiseSourceWithTimeout } from 'promises';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { Interactive } from 'interactive';
import { OnDeviceAwake } from 'on-device-awake';
import { Log, LogLevel, LogScope } from 'logging';
import { Timeout } from 'timeout';

const LogScope: LogScope = 'AudioContextSource';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const errorLog = Log.get(LogScope, LogLevel.Error);

const MaxWarmupTimeMs = 1000;
const MaxResumeTimeMs = 1000;
const WakeUpDetectionIntervalMs = 300;
const TestIntervalMs = 40;

export class AudioContextSource implements Disposable {
    private _isDisposed = false;
    private _onDeviceAwakeHandler?: EventHandler<void>;
    private _deviceWokeUpAt: number;
    private _breakEvents = new EventHandlerSet<void>();
    private _whenAudioContextReady = new PromiseSource<AudioContext>();
    private readonly _whenDisposed: Promise<void>;

    constructor() {
        this._onDeviceAwakeHandler = OnDeviceAwake.events.add(() => this.onDeviceAwake());
        this._whenDisposed = this.maintainUnbroken();
    }

    public dispose(): void {
        if (this._isDisposed)
            return;

        this._isDisposed = true;
        this._onDeviceAwakeHandler?.dispose();
        this._onDeviceAwakeHandler = null;
        this.markBroken(); // Just to make sure we signal run() to stop
    }

    public whenDisposed(): Promise<void> {
        return this._whenDisposed;
    }

    public get(): Promise<AudioContext> {
        this.throwIfDisposed();
        return this._whenAudioContextReady;
    }

    public markBroken(): void {
        if (this._whenAudioContextReady.isCompleted())
            this._breakEvents.trigger(undefined);
    }

    // Protected methods

    protected async maintainUnbroken(): Promise<void> {
        let audioContext: AudioContext = null;
        for (;;) { // Renew loop
            try {
                this.throwIfDisposed();

                audioContext = await this.create();
                this.throwIfDisposed();

                await this.warmup(audioContext);
                await this.test(audioContext);
                this.throwIfDisposed();

                for (;;) { // Fix loop
                    this._whenAudioContextReady.resolve(audioContext);
                    await this._breakEvents.whenNext();
                    this.throwIfDisposed();

                    // Let's try to fix broken AudioContext
                    debugLog?.log(`run: AudioContext is marked as broken`);
                    this._whenAudioContextReady = new PromiseSource<AudioContext>();
                    try {
                        await this.test(audioContext);
                        this.throwIfDisposed();

                        continue; // Test passed, we're fine to expose it
                    }
                    catch (e) {
                        warnLog?.log(`run: AudioContext is actually broken:`, e);
                    }

                    await this.fix(audioContext);
                    this.throwIfDisposed();
                }
            }
            catch (e) {
                if (this._isDisposed)
                    return;

                warnLog?.log(`run: error:`, e);
            }
            finally {
                await this.close(audioContext);
                if (this._whenAudioContextReady.isCompleted() && !this._isDisposed)
                    this._whenAudioContextReady = new PromiseSource<AudioContext>();
            }
        }
    }

    protected async create(): Promise<AudioContext> {
        debugLog?.log(`create()`)
        const audioContext = new AudioContext({
            latencyHint: 'interactive',
            sampleRate: 48000,
        });

        await Interactive.whenInteractive();
        await audioContext.resume();
        if (audioContext.state !== 'running') {
            await this.close(audioContext);
            throw `${LogScope}.create: AudioContext.resume failed.`;
        }

        debugLog?.log(`create: loading modules`)
        const whenModule1 = audioContext.audioWorklet.addModule('/dist/feederWorklet.js');
        const whenModule2 = audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js');
        const whenModule3 = audioContext.audioWorklet.addModule('/dist/vadWorklet.js');
        await Promise.all([whenModule1, whenModule2, whenModule3]);
        return audioContext;
    }

    protected async warmup(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`warmup(), AudioContext:`, audioContext);

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

        try {
            const whenWarmedUp = new PromiseSourceWithTimeout<void>();
            node.port.postMessage('stop');
            node.port.onmessage = (ev: MessageEvent<string>): void => {
                if (ev.data === 'stopped')
                    whenWarmedUp.resolve(undefined);
                else
                    warnLog?.log(`warmup: unsupported message from warm up worklet`);
            };
            whenWarmedUp.setTimeout(MaxWarmupTimeMs, () => {
                whenWarmedUp.reject(`${LogScope}.warmup: couldn't complete warm-up on time.`);
            })
            await whenWarmedUp;
        }
        finally {
            node.disconnect();
            node.port.onmessage = null;
            node.port.close();
        }
    }

    protected async test(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`test(): AudioContext:`, audioContext);
        if (audioContext.state !== 'running')
            throw `${LogScope}.test: AudioContext isn't running.`;

        const now = audioContext.currentTime;
        await delayAsync(TestIntervalMs);
        if (audioContext.state !== 'running')
            throw `${LogScope}.test: AudioContext isn't running.`;
        if (audioContext.currentTime == now) // AudioContext isn't running
            throw `${LogScope}.test: AudioContext is running, but didn't pass currentTime test.`;
    }

    protected async fix(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`fix(): AudioContext:`, audioContext);
        const isWakeUpCall = (Date.now() - this._deviceWokeUpAt) <= WakeUpDetectionIntervalMs;

        const tryResume: () => Promise<boolean> = async () => {
            try {
                if (audioContext.state !== 'suspended')
                    await audioContext.suspend();
                await Interactive.whenInteractive();
                const resumeTask = audioContext.resume().then(() => true);
                const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
                if (!await Promise.race([resumeTask, timerTask])) {
                    warnLog?.log(`fix: tryResume timed out`);
                    return false;
                }

                await this.test(audioContext);
            }
            catch (e) {
                warnLog?.log(`fix: tryResume failed:`, e);
                return false;
            }
        }

        if (await tryResume())
            return;

        if (isWakeUpCall && !Interactive.isAlwaysInteractive) {
            Interactive.isInteractive = false;
            if (await tryResume())
                return;
        }

        throw `${LogScope}.fix: couldn't resume AudioContext.`;
    }

    protected async close(audioContext?: AudioContext): Promise<void> {
        debugLog?.log(`close(): AudioContext:`, audioContext);
        if (!audioContext)
            return;
        if (audioContext.state === 'closed')
            return;

        try {
            await audioContext.close();
        }
        catch (e) {
            warnLog?.log(`close: failed to close AudioContext:`, e)
        }
    }

    private throwIfDisposed() {
        if (this._isDisposed)
            throw `${LogScope}.throwIfDisposed: already disposed.`;
    }

    // Event handlers

    private onDeviceAwake() {
        this._deviceWokeUpAt = Date.now();
        this.markBroken();
    }
}

// Init

export const audioContextSource = new AudioContextSource();
window['audioContextSource'] = audioContextSource;

