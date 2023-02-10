import { Disposable } from 'disposable';
import { delayAsync, PromiseSource, PromiseSourceWithTimeout, serialize } from 'promises';
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
const MaxResumeTimeMs = 600;
const MaxResumeAttemptsDurationMs = 3000;
const MaxSuspendTimeMs = 300;
const MaxInteractionWaitTimeMs = 60_000;
const TestIntervalMs = 40;
const WakeUpDetectionIntervalMs = 5000;

export class AudioContextSource implements Disposable {
    private _audioContext: AudioContext | null = null;
    private _isDisposed = false;
    private _onDeviceAwakeHandler: EventHandler<void>;
    private _deviceWokeUpAt = 0;
    private _isInteractiveWasReset = false;
    private _changeCount = 0;
    private _isBeingResumed = false;
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

    // NOTE(AY): both markReady and markNotReady are written so that
    // they can be called repeatedly. Subsequent calls to them produce no effect.

    // Must be private, but good to keep it near markNotReady
    private markReady(audioContext: AudioContext | null) {
        Interactive.isInteractive = true;
        if (this._whenReady.isCompleted())
            return; // Already ready

        this._audioContext = audioContext;
        this._changeCount++;
        debugLog?.log(`markReady(): #${this._changeCount}, AudioContext:`, audioContext);

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
        let lastTestTimestamp = Date.now();

        for (;;) { // Renew loop
            if (this._isDisposed)
                return;

            let audioContext = this._audioContext;
            try {
                if (audioContext === null || audioContext.state === 'closed') {
                    audioContext = await this.create();
                    await this.warmup(audioContext);
                    await this.test(audioContext);
                    this.markReady(audioContext);
                }
                this.throwIfDisposed();

                // noinspection InfiniteLoopJS
                for (;;) { // Fix loop
                    this.throwIfDisposed();

                    const currentTimestamp = Date.now();
                    const timePassedSinceTest = currentTimestamp - lastTestTimestamp;
                    if (timePassedSinceTest < MaintainCyclePeriodMs) {
                        await delayAsync(MaintainCyclePeriodMs - timePassedSinceTest);
                    }
                    else {
                        const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                        await Promise.race([this._whenNotReady, whenDelayCompleted]);
                    }
                    this.throwIfDisposed();

                    // Let's try to test whether AudioContext is broken and fix
                    try {
                        lastTestTimestamp = Date.now();
                        await this.test(audioContext);

                        // Test passed, we're fine to keep it
                        if (this._whenNotReady.isCompleted()) {
                            // Might be in "not ready" state here
                            this.markReady(audioContext);
                        }
                        else if(!this._whenReady.isCompleted()) {
                            // Was not ready yet
                            this.markReady(audioContext);
                        }
                        continue;
                    }
                    catch (e) {
                        warnLog?.log(`maintain: AudioContext is actually broken:`, e);
                        this.markNotReady();
                    }

                    this.throwIfDisposed();
                    const fixedContext = await this.fix(audioContext);
                    this.markReady(fixedContext);
                }
            }
            catch (e) {
                warnLog?.log(`maintain: error:`, e);
            }
            finally {
                this.markNotReady();
            }
        }
    }

    protected async create(): Promise<AudioContext> {
        debugLog?.log(`create()`);

        const audioContext = await this.refreshContext(null);
        if (audioContext.state !== 'running') {
            await this.close(audioContext);
            throw `${LogScope}.create: AudioContext.resume failed.`;
        }

        debugLog?.log(`create: loading modules`);
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

        // Since AudioContext is fine, we should report we're interactive at this point
        Interactive.isInteractive = true;
    }

    protected async fix(audioContext: AudioContext): Promise<AudioContext> {
        debugLog?.log(`fix(): AudioContext:`, audioContext);

        try {
            if (!await this.trySuspend(audioContext))
                throw `${LogScope}.resume: couldn't suspend AudioContext`;
            const refreshedContext = await this.refreshContext(audioContext);
            await this.test(refreshedContext);
            debugLog?.log(`fix: success`, );
            return refreshedContext;
        }
        catch (e) {
            warnLog?.log(`fix: failed, error:`, e);
            throw e;
        }
    }

    protected async refreshContext(audioContext: AudioContext | null): Promise<AudioContext> {
        debugLog?.log(`refreshContext(): AudioContext:`, audioContext);
        if (audioContext && audioContext.state === 'running')
            return audioContext;

        // Create and resume audio context without waiting for user interaction iof not required
        if (Interactive.isAlwaysInteractive) {
            if (!audioContext)
                audioContext = new AudioContext({
                    latencyHint: 'interactive',
                    sampleRate: 48000,
                });
            if (!await this.tryResume(audioContext))
                throw `${LogScope}.resume: couldn't resume w/o interaction, but tryInteractive == false`;
            return audioContext;
        }

        // Resume can be called during user interaction only
        const isWakeUp = this.isWakeUp();
        if (isWakeUp && !this._isInteractiveWasReset) {
            this._isInteractiveWasReset = true;
            Interactive.isInteractive = false;
        }

        debugLog?.log(`resume: waiting for interaction`);
        const contextTask = new PromiseSource<AudioContext | null>();
        const handler = Interactive.interactionEvents.add( () => {
            if (!audioContext)
                audioContext = new AudioContext({
                    latencyHint: 'interactive',
                    sampleRate: 48000,
                });
            this.tryResume(audioContext)
                .then(
                    success => {
                        // Let's wait for yet another interaction attempt if not resumed
                        if (success)
                            contextTask.resolve(audioContext);
                        else {
                            // Recreate AudioContext if failed several resume attempts during MaxResumeAttemptsDurationMs
                            const lastResumeAttemptAt = audioContext['lastResumeAttemptAt'] as number;
                            const now = Date.now();
                            if (lastResumeAttemptAt && now - lastResumeAttemptAt > MaxResumeAttemptsDurationMs) {
                                void audioContext.close();
                                contextTask.resolve(null);
                            }
                            else
                                audioContext['lastResumeAttemptAt'] = Date.now();
                        }
                    },
                    reason => {
                        warnLog?.log(reason, 'tryResume failed');
                        void audioContext.close();
                        contextTask.resolve(null);
                    });
        });
        try {
            const timerTask = delayAsync(MaxInteractionWaitTimeMs).then(() => null as (AudioContext | null));
            const refreshedContext = await Promise.race([contextTask, timerTask]);
            if (!refreshedContext)
                throw `${LogScope}.resume: timed out while waiting for interaction`;
            if (refreshedContext.state !== 'running')
                // Unable to resume context - let's close and recreate later
                await refreshedContext.close();

            debugLog?.log(`resume: succeeded on interaction`);
            return refreshedContext;
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
        const suspendTask = audioContext.suspend().then(() => true);
        const timerTask = delayAsync(MaxSuspendTimeMs).then(() => false);
        if (await Promise.race([suspendTask, timerTask])) {
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
        if (this._isBeingResumed)
            return false;

        try {
            this._isBeingResumed = true;

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
            } else {
                debugLog?.log(`tryResume: timed out`);
                return false;
            }
        }
        finally {
            this._isBeingResumed = false;
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
        debugLog?.log(`onDeviceAwake()`);
        this._deviceWokeUpAt = Date.now();
        this._isInteractiveWasReset = false;
        // close current AudioContext as it might be corrupted and can produce clicking sound
        void this._audioContext.close();
        this.markNotReady();
    }
}

// Init

export const audioContextSource = new AudioContextSource();
globalThis['audioContextSource'] = audioContextSource;

