import { Disposable } from 'disposable';
import { delayAsync, PromiseSource, PromiseSourceWithTimeout } from 'promises';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { Interactive } from 'interactive';
import { OnDeviceAwake } from 'on-device-awake';
import { AudioContextRef } from 'audio-context-ref';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioContextSource';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const errorLog = Log.get(LogScope, LogLevel.Error);

const MaintainCyclePeriodMs = 2000;
const MaxWarmupTimeMs = 1000;
const MaxResumeTimeMs = 300;
const MaxSuspendTimeMs = 300;
const MaxInteractionWaitTimeMs = 60_000;
const TestIntervalMs = 40;
const WakeUpDetectionIntervalMs = 5000;

export class AudioContextSource implements Disposable {
    private _isDisposed = false;
    private _onDeviceAwakeHandler: EventHandler<void>;
    private _deviceWokeUpAt: number;
    private _changeCount = 0;
    private _whenReady = new PromiseSource<AudioContext | null>();
    private _whenNotReady = new PromiseSource<void>();
    private readonly _whenDisposed: Promise<void>;

    public readonly changedEvents = new EventHandlerSet<AudioContext | null>();

    constructor() {
        this._onDeviceAwakeHandler = OnDeviceAwake.events.add(() => this.onDeviceAwake());
        this._whenDisposed = this.maintain();
    }

    public get refCount() {
        return this.changedEvents.count;
    }

    public dispose(): void {
        if (this._isDisposed)
            return;

        this._isDisposed = true;
        this.markNotReady(); // This ensures _whenReady is not completed
        this.markReady(null); // Final "ready" event produces null AudioContext
        this._onDeviceAwakeHandler.dispose();
    }

    public whenDisposed(): Promise<void> {
        return this._whenDisposed;
    }

    public async getRef(): Promise<AudioContextRef> {
        debugLog?.log('-> getRef()');
        this.throwIfDisposed();
        try {
            const audioContext = await this._whenReady;
            this.throwIfDisposed();
            return new AudioContextRef(this, audioContext);
        }
        finally {
            debugLog?.log('<- getRef, refCount:', this.refCount);
        }
    }

    public async get(): Promise<AudioContext> {
        debugLog?.log('get()');
        this.throwIfDisposed();
        const audioContext = await this._whenReady;
        this.throwIfDisposed();
        return audioContext;
    }

    // NOTE(AY): both markReady and markNotReady are written so that
    // they can be called repeatedly. Subsequent calls to them produce no effect.

    // Must be private, but good to keep it near markNotReady
    private markReady(audioContext: AudioContext | null) {
        if (this._whenReady.isCompleted())
            return; // Already ready

        this._changeCount++;
        debugLog?.log(`markReady(): #${this._changeCount}, AudioContext:`, audioContext);
        Interactive.isInteractive = true;

        // _whenNotReady must be replaced first
        if (this._whenNotReady.isCompleted())
            this._whenNotReady = new PromiseSource<void>();

        this._whenReady.resolve(audioContext);
        if (this._changeCount > 1)
            this.changedEvents.trigger(audioContext)
    }

    public markNotReady(): void {
        if (!this._whenReady.isCompleted())
            return; // Already not ready

        debugLog?.log(`markNotReady()`);

        // _whenReady must be replaced first
        this._whenReady = new PromiseSource<AudioContext>();

        if (!this._whenNotReady.isCompleted())
            this._whenNotReady.resolve(undefined);
    }

    // Protected methods

    protected async maintain(): Promise<void> {
        let audioContext: AudioContext = null;
        for (;;) { // Renew loop
            if (this._isDisposed)
                return;

            try {
                audioContext = await this.create();
                this.throwIfDisposed();

                await this.warmup(audioContext);
                await this.test(audioContext);

                // noinspection InfiniteLoopJS
                for (;;) { // Fix loop
                    this.throwIfDisposed();
                    this.markReady(audioContext);

                    const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                    await Promise.race([this._whenNotReady, whenDelayCompleted]);
                    this.throwIfDisposed();

                    // Let's try to test whether AudioContext is broken and fix
                    try {
                        await this.test(audioContext);

                        // Test passed, we're fine to keep it
                        if (this._whenNotReady.isCompleted()) // Might be in "not ready" state here
                            this.markReady(audioContext);
                        continue;
                    }
                    catch (e) {
                        warnLog?.log(`maintain: AudioContext is actually broken:`, e);
                        this.markNotReady();
                    }

                    this.throwIfDisposed();
                    await this.fix(audioContext);
                }
            }
            catch (e) {
                warnLog?.log(`maintain: error:`, e);
            }
            finally {
                this.markNotReady();
                await this.close(audioContext);
            }
        }
    }

    protected async create(): Promise<AudioContext> {
        debugLog?.log(`create()`)

        const audioContext = new AudioContext({
            latencyHint: 'interactive',
            sampleRate: 48000,
        });
        await this.resume(audioContext, true);
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

        try {
            await this.resume(audioContext, false);
            await this.test(audioContext);
            debugLog?.log(`fix: success`, );
            return;
        }
        catch (e) {
            warnLog?.log(`fix: failed, error:`, e);
            throw e;
        }
    }

    protected async resume(audioContext: AudioContext, tryInteractive: boolean): Promise<void> {
        debugLog?.log(`resume(): AudioContext:`, audioContext);
        if (audioContext.state === 'running')
            return;

        const isWakeUp = this.isWakeUp()

        if (!await this.trySuspend(audioContext))
            throw `${LogScope}.resume: couldn't suspend AudioContext`;
        if (await this.tryResume(audioContext))
            return;

        // Couldn't resume
        if (isWakeUp)
            Interactive.isInteractive = false;
        if (!tryInteractive)
            throw `${LogScope}.resume: couldn't resume w/o interaction, but tryInteractive == false`;

        debugLog?.log(`resume: waiting for interaction`);
        const whenResumed = new PromiseSource<boolean>();
        const handler = Interactive.interactionEvents.add(() => {
            audioContext.resume().then(
                () => audioContext.state === 'running' ? whenResumed.resolve(true) : undefined,
                () => undefined);
        });
        try {
            const timerTask = delayAsync(MaxInteractionWaitTimeMs).then(() => false);
            if (!await Promise.race([whenResumed, timerTask]))
                throw `${LogScope}.resume: timed out while waiting for interaction`;

            debugLog?.log(`resume: succeeded on interaction`);
            return;
        }
        finally {
            handler.dispose();
        }
    }

    protected async trySuspend(audioContext: AudioContext): Promise<boolean> {
        if (audioContext.state === 'suspended') {
            debugLog?.log(`trySuspend(): already suspended, AudioContext:`, audioContext);
            return true;
        }

        debugLog?.log(`trySuspend(): AudioContext:`, audioContext);
        const resumeTask = audioContext.suspend().then(() => true);
        const timerTask = delayAsync(MaxSuspendTimeMs).then(() => false);
        if (await Promise.race([resumeTask, timerTask])) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            if (audioContext.state !== 'suspended') {
                debugLog?.log(`trySuspend: completed suspend, but AudioContext.state != 'suspended'`);
                return false;
            }
            debugLog?.log(`trySuspend: success`);
            return true;
        }
        else {
            debugLog?.log(`trySuspend: timed out`);
            return false;
        }
    }

    protected async tryResume(audioContext: AudioContext): Promise<boolean> {
        if (audioContext.state === 'running') {
            debugLog?.log(`tryResume(): already resumed, AudioContext:`, audioContext);
            return true;
        }

        debugLog?.log(`tryResume(): AudioContext:`, audioContext);
        const resumeTask = audioContext.resume().then(() => true);
        const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
        if (await Promise.race([resumeTask, timerTask])) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            if (audioContext.state !== 'running') {
                debugLog?.log(`tryResume: completed resume, but AudioContext.state != 'running'`);
                return false;
            }
            debugLog?.log(`tryResume: success`);
            return true;
        }
        else {
            debugLog?.log(`tryResume: timed out`);
            return false;
        }
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

    private isWakeUp() {
        return (Date.now() - this._deviceWokeUpAt) <= WakeUpDetectionIntervalMs;
    }

    // Event handlers

    private onDeviceAwake() {
        this._deviceWokeUpAt = Date.now();
        this.markNotReady();
    }
}

// Init

export const audioContextSource = new AudioContextSource();
window['audioContextSource'] = audioContextSource;

