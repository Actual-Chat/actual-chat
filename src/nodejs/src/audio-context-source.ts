import { AudioContextRef } from 'audio-context-ref';
import { BrowserInfo } from '../../dotnet/UI.Blazor/Services/BrowserInfo/browser-info';
import { Disposable } from 'disposable';
import { delayAsync, PromiseSource, PromiseSourceWithTimeout, TimedOut } from 'promises';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { Interactive } from 'interactive';
import { OnDeviceAwake } from 'on-device-awake';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioContextSource';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const errorLog = Log.get(LogScope, LogLevel.Error);

const MaintainCyclePeriodMs = 2000;
const FixCyclePeriodMs = 300;
const MaxWarmupTimeMs = 2000;
const MaxResumeTimeMs = 600;
const MaxResumeCount = 60;
const MaxInteractiveResumeCount = 3;
const MaxSuspendTimeMs = 300;
const MaxInteractionWaitTimeMs = 60_000;
const ShortTestIntervalMs = 60;
const LongTestIntervalMs = 1000;
const SilencePlaybackDuration = 0.280;
const WakeUpDetectionIntervalMs = 5000;

export class AudioContextSource implements Disposable {
    private _audioContext: AudioContext | null = null;
    private _isDisposed = false;
    private _onDeviceAwakeHandler: EventHandler<void>;
    private _deviceWokeUpAt = 0;
    private _isInteractiveWasReset = false;
    private _changeCount = 0;
    private _resumeCount = 0;
    private _interactiveResumeCount = 0;
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
        // Invariant it maintains on exit:
        // - _whenReady is completed - and if it's completed, it exists immediately
        // - _whenNotReady is NOT completed
        // In other words, _whenReady state is "ground truth", _whenNotReady state is secondary

        Interactive.isInteractive = true;
        if (this._whenReady.isCompleted())
            return; // Already ready

        this._changeCount++;
        this._audioContext = audioContext;
        debugLog?.log(`markReady: #${this._changeCount}, AudioContext:`, audioContext);

        // _whenNotReady must be replaced first
        if (this._whenNotReady.isCompleted())
            this._whenNotReady = new PromiseSource<void>();

        this._whenReady.resolve(audioContext);
        if (this._changeCount > 1)
            this.changedEvents.trigger(audioContext)
    }

    public markNotReady(): void {
        // Invariant it maintains on exit:
        // - _whenReady is NOT completed - and if it's NOT completed, it exists immediately
        // - _whenNotReady is completed completed
        // In other words, _whenReady state is "ground truth", _whenNotReady state is secondary

        if (!this._whenReady.isCompleted())
            return; // Already not ready

        debugLog?.log(`markNotReady`);

        this._audioContext = null;
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

            let audioContext: AudioContext = null;
            try {
                audioContext = await this.create();
                await this.warmup(audioContext);
                this.markReady(audioContext);
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
                        if (BrowserInfo.appKind !== 'MauiApp')
                            // Skip test for MAUI as it doesn't exclusively use audio output or microphone
                            // nevertheless `test` will be called as part of `fix` call
                            await this.test(audioContext, true);
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        this.markReady(audioContext);
                        continue;
                    }
                    catch (e) {
                        warnLog?.log(`maintain: AudioContext is actually broken:`, e);
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        this.markNotReady();
                    }

                    for(;;) {
                        this.throwIfDisposed();
                        this.throwIfClosed(audioContext);
                        this.throwIfTooManyResumes();
                        try {
                            await this.fix(audioContext);
                            break;
                        }
                        catch (e) {
                            await delayAsync(FixCyclePeriodMs);
                        }
                    }
                    this.markReady(audioContext);
                }
            }
            catch (e) {
                warnLog?.log(`maintain: error:`, e);
            }
            finally {
                this.markNotReady();
                await this.closeSilently(audioContext);
            }
        }
    }

    protected async create(): Promise<AudioContext> {
        debugLog?.log(`create`);

        this._resumeCount = 0;
        this._interactiveResumeCount = 0;
        // Try to create audio context early w/o waiting for user interaction.
        // It might be in suspended state in this case.
        const audioContext = new AudioContext({
            latencyHint: 'interactive',
            sampleRate: 48000,
        });
        try {
            await this.interactiveResume(audioContext);

            debugLog?.log(`create: loading modules`);
            const whenModule1 = audioContext.audioWorklet.addModule('/dist/feederWorklet.js');
            const whenModule2 = audioContext.audioWorklet.addModule('/dist/opusEncoderWorklet.js');
            const whenModule3 = audioContext.audioWorklet.addModule('/dist/vadWorklet.js');
            await Promise.all([whenModule1, whenModule2, whenModule3]);
            return audioContext;
        }
        catch (e) {
            await this.closeSilently(audioContext);
            throw e;
        }
    }

    protected async warmup(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`warmup, AudioContext:`, audioContext);

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

    protected async test(audioContext: AudioContext, isLongTest = false): Promise<void> {
        if (audioContext.state !== 'running')
            throw `${LogScope}.test: AudioContext isn't running.`;

        const lastTime = audioContext.currentTime;
        const testCycleCount = 5;
        const testIntervalMs = isLongTest ? ShortTestIntervalMs : LongTestIntervalMs;
        for (let i = 0; i < testCycleCount; i++) {
            await delayAsync(testIntervalMs);
            if (audioContext.state !== 'running')
                throw `${LogScope}.test: AudioContext isn't running.`;
            if (audioContext.currentTime != lastTime)
                break;
            // play silent audio and check state
            else if (this.isRunning(audioContext)) {
                debugLog?.log(`test: AudioContext is running, but currentTime is not changing.`);
            }
        }
        if (audioContext.currentTime == lastTime) // AudioContext isn't running
            throw `${LogScope}.test: AudioContext is running, but didn't pass currentTime test.`;
    }

    protected async fix(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`fix:`, audioContext);

        try {
            if (!await this.trySuspend(audioContext)) {
                // noinspection ExceptionCaughtLocallyJS
                throw `${LogScope}.fix: couldn't suspend AudioContext`;
            }
            await this.interactiveResume(audioContext);
            await this.test(audioContext);
            debugLog?.log(`fix: success`, );
        }
        catch (e) {
            warnLog?.log(`fix: failed, error:`, e);
            throw e;
        }
    }

    protected async interactiveResume(audioContext: AudioContext): Promise<void> {
        debugLog?.log(`interactiveResume:`, audioContext);
        if (audioContext && this.isRunning(audioContext)) {
            debugLog?.log(`interactiveResume: succeeded (AudioContext is already in running state)`);
            return;
        }

        await BrowserInfo.whenReady; // This is where isAlwaysInteractive flag gets set - it checked further
        if (Interactive.isAlwaysInteractive) {
            debugLog?.log(`interactiveResume: Interactive.isAlwaysInteractive == true`);
            await this.resume(audioContext, false);
        }
        else {
            // Resume can be called during user interaction only
            const isWakeUp = this.isWakeUp();
            if (isWakeUp && !this._isInteractiveWasReset) {
                this._isInteractiveWasReset = true;
                Interactive.isInteractive = false;
                debugLog?.log(`interactiveResume: Interactive.isInteractive was reset on wake up`);
            }
        }

        debugLog?.log(`interactiveResume: waiting for interaction`);
        const resumeTask = new PromiseSource<boolean>();
        // Keep user gesture stack without async!!!
        const handler = Interactive.interactionEvents.add( () => {
            // this resume should be called without async in the same sync stack as user gesture!!!
            this.resume(audioContext, true)
                .then(
                    () => resumeTask.resolve(true),
                    reason => {
                        warnLog?.log(reason, 'resume() failed with an error');
                        resumeTask.reject(reason);
                    });
        });
        try {
            const timerTask = delayAsync(MaxInteractionWaitTimeMs).then(() => false);
            const success = await Promise.race([resumeTask, timerTask]);
            if (!success)
                throw `${LogScope}.interactiveResume: timed out while waiting for interaction`;

            debugLog?.log(`interactiveResume: succeeded on interaction`);
        }
        finally {
            handler.dispose();
        }
    }

    private async resume(audioContext: AudioContext, isInteractive: boolean): Promise<void> {
        debugLog?.log(`resume:`, audioContext);

        this._resumeCount++;
        if (isInteractive)
            this._interactiveResumeCount++;

        if (this.isRunning(audioContext)) {
            debugLog?.log(`resume: already resumed, AudioContext:`, audioContext);
            return;
        }

        const resumeTask = audioContext.resume().then(() => true);
        const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
        if (!await Promise.race([resumeTask, timerTask]))
            throw `${LogScope}.resume: AudioContext.resume() has timed out`;
        if (!this.isRunning(audioContext))
            throw `${LogScope}.resume: completed resume, but AudioContext.state != 'running'`;

        debugLog?.log(`resume: resumed, AudioContext:`, audioContext);
    }

    protected async trySuspend(audioContext: AudioContext): Promise<boolean> {
        if (audioContext.state === 'suspended') {
            debugLog?.log(`trySuspend: already suspended, AudioContext:`, audioContext);
            return true;
        }
        if (audioContext.state === 'closed') {
            debugLog?.log(`trySuspend: unable to suspend closed AudioContext`);
            return false;
        }

        debugLog?.log(`trySuspend:`, audioContext);
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


    protected isRunning(audioContext: AudioContext): boolean {
        // This method addresses some weird issues in how AudioContext behaves in different browsers:
        // - Chromium 110 AudioContext can be in 'running' even after
        //   calling constructor, and even without user interaction.
        // - Safari doesn't start incrementing 'currentTime' after 'resume' call,
        //   so we have to warm it up w/ silent audio
        if (audioContext.state !== 'running')
            return false;

        const silenceBuffer = audioContext['silenceBuffer'] as AudioBuffer ?? this.createSilenceBuffer(audioContext);
        const source = audioContext.createBufferSource();
        source.buffer = silenceBuffer;
        source.connect(audioContext.destination);
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        source.onended = () => source.disconnect();
        audioContext['silenceBuffer'] = silenceBuffer;
        source.start(0);
        // Schedule to stop silence playback in the future
        source.stop(audioContext.currentTime + SilencePlaybackDuration);
        // NOTE(AK): Somehow - sporadically - currentTime starts ticking only when you log the context!
        console.log(`AudioContext is:`, audioContext, `, its currentTime:`, audioContext.currentTime);
        return audioContext.state === 'running';
    }

    protected async closeSilently(audioContext?: AudioContext): Promise<void> {
        debugLog?.log(`close:`, audioContext);
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

    private createSilenceBuffer(audioContext: AudioContext): AudioBuffer {
        return audioContext.createBuffer(1, 1, 48000);
    }

    private throwIfTooManyResumes(): void {
        if (this._resumeCount >= MaxResumeCount)
            throw `maintain: resume attempt count is too high (${this._resumeCount})`;
        if (this._interactiveResumeCount >= MaxInteractiveResumeCount)
            throw `maintain: interactive resume attempt count is too high (${this._interactiveResumeCount})`;
    }

    private throwIfDisposed(): void {
        if (this._isDisposed)
            throw `${LogScope}.throwIfDisposed: already disposed.`;
    }

    private throwIfClosed(audioContext: AudioContext): void {
        if (audioContext.state === 'closed')
            throw `${LogScope}.throwIfClosed: context is closed.`;
    }

    private isWakeUp(): boolean {
        return (Date.now() - this._deviceWokeUpAt) <= WakeUpDetectionIntervalMs;
    }

    // Event handlers

    private onDeviceAwake() {
        debugLog?.log(`onDeviceAwake`);
        this._deviceWokeUpAt = Date.now();
        this._isInteractiveWasReset = false;
        // close current AudioContext as it might be corrupted and can produce clicking sound
        void this.closeSilently(this._audioContext);
        this.markNotReady();
    }
}

// Init

export const audioContextSource = new AudioContextSource();
globalThis['audioContextSource'] = audioContextSource;

