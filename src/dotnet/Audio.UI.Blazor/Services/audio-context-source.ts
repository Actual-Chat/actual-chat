import { BrowserInfo } from '../../UI.Blazor/Services/BrowserInfo/browser-info';
import {
    Cancelled,
    delayAsync,
    PromiseSource,
    PromiseSourceWithTimeout, ResolvedPromise,
    waitAsync,
} from 'promises';
import { EventHandler } from 'event-handling';
import { Interactive } from 'interactive';
import { OnDeviceAwake } from 'on-device-awake';
import { Log } from 'logging';
import { Versioning } from 'versioning';
import { AudioContextRef, AudioContextRefOptions } from './audio-context-ref';
import { Subject } from 'rxjs';
import {AudioContextDestinationFallback} from "./audio-context-destination-fallback";

const { logScope, debugLog, warnLog } = Log.get('AudioContextSource');

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

const Debug = {
    brokenKey: 'debugging_isBroken',
}

export class AudioContextSource {
    private readonly refCounts: Map<string, number> = new Map<string, number>();
    private readonly fallbackDestination?: AudioContextDestinationFallback;
    private readonly _contextCreated$: Subject<AudioContext> = new Subject<AudioContext>();
    private readonly _contextClosing$: Subject<AudioContext> = new Subject<AudioContext>();

    private _context: AudioContext | null = null;
    private onDeviceAwakeHandler: EventHandler<number>;
    private deviceWokeUpAt = 0;
    private isInteractiveWasReset = false;
    private resumeCount = 0;
    private interactiveResumeCount = 0;
    private whenInitializedInteractively: Promise<void> | null = null;
    private _refCount = 0;
    private _whenReady = new PromiseSource<AudioContext | null>();
    private _whenNotReady = new PromiseSource<void>();

    public readonly contextCreated$ = this._contextCreated$.asObservable();
    public readonly contextClosing$ = this._contextClosing$.asObservable();

    // Key properties
    public get context(): AudioContext { return this._context; }
    public get destination(): AudioNode { return this.fallbackDestination?.destination ?? this._context.destination; }
    public get refCount(): number { return this._refCount }

    constructor() {
        this.onDeviceAwakeHandler = OnDeviceAwake.events.add(() => this.onDeviceAwake());
        if (AudioContextDestinationFallback.isRequired)
            this.fallbackDestination = new AudioContextDestinationFallback();
        void this.maintain();
    }

    public getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef {
        this.incrementRefCount(operationName);
        const result = new AudioContextRef(this, operationName, options);
        void result.whenDisposed().then(() => this.decrementRefCount(operationName));
        return result;
    }

    public whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext> {
        return waitAsync(this._whenReady, cancel);
    }

    public whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void> {
        if (!context || this._context != context)
            return ResolvedPromise.Void;

        return waitAsync(this._whenNotReady, cancel);
    }

    public async initContextInteractively(): Promise<void> {
        if (this._context && this._context.state === 'running') {
            debugLog?.log(`initContextInteractively: already running`);
            return; // Already ready
        }

        if (this.whenInitializedInteractively)
            return; // Already being initialized

        const whenInitializedInteractively = new PromiseSource<void>();
        try {
            this.whenInitializedInteractively = whenInitializedInteractively;
            const context = await this.create(true);
            // skip warmup as we need the context ASAP - e.g. after clicking on the recording button
            this.markReady(context);
        }
        finally {
            whenInitializedInteractively.resolve(undefined);
            this.whenInitializedInteractively = null;
        }
    }

    // Must be private, but good to keep it near markNotReady
    private markReady(context: AudioContext | null) {
        // Invariant it maintains on exit:
        // - _context != null
        // - _whenReady is completed
        // - _whenNotReady is NOT completed.

        Interactive.isInteractive = true;
        if (this._context)
            return; // Already ready

        this._context = context;
        debugLog?.log(`markReady: AudioContext:`, Log.ref(context));

        // _whenNotReady must be replaced first
        if (this._whenNotReady.isCompleted())
            this._whenNotReady = new PromiseSource<void>();

        this._whenReady.resolve(context);
    }

    private markNotReady(): void {
        // Invariant it maintains on exit:
        // - _context == null
        // - _whenReady is NOT completed
        // - _whenNotReady is completed.

        const audioContext = this._context;
        if (!audioContext)
            return; // Already not ready

        this._context = null;
        debugLog?.log(`markNotReady`);

        // _whenReady must be replaced first
        this._whenReady = new PromiseSource<AudioContext>();

        if (!this._whenNotReady.isCompleted())
            this._whenNotReady.resolve(undefined);
    }

    public break() {
        if (!this.context) {
            warnLog?.log(`break: no AudioContext, so nothing to break`);
            return;
        }

        this.context[Debug.brokenKey] = true;
        warnLog?.log(`break: done`);
    }

    // Protected methods

    protected async maintain(): Promise<void> {
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        await delayAsync(300);
        // noinspection InfiniteLoopJS
        for (;;) { // Renew loop
            let context: AudioContext = null;
            try {
                let whenInitializedInteractively = this.whenInitializedInteractively as PromiseSource<void>;
                if (!whenInitializedInteractively) {
                    whenInitializedInteractively = new PromiseSource<void>();
                    try {
                        this.whenInitializedInteractively = whenInitializedInteractively;
                        context = await this.create();
                        await this.warmup(context);
                        this.markReady(context);
                    }
                    finally {
                        whenInitializedInteractively.resolve(undefined);
                        this.whenInitializedInteractively = null;
                    }
                }
                else {
                    await whenInitializedInteractively;
                    context = this._context;
                    if (!context)
                        continue;
                }
                let lastTestAt = Date.now();

                // noinspection InfiniteLoopJS
                for (;;) { // Fix loop
                    const minDelay = lastTestAt + MaintainCyclePeriodMs - Date.now();
                    if (minDelay > 0) {
                        await delayAsync(minDelay);
                    }
                    else {
                        const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                        await Promise.race([this._whenNotReady, whenDelayCompleted]);
                    }

                    // Let's try to test whether AudioContext is broken and fix
                    try {
                        lastTestAt = Date.now();
                        if (BrowserInfo.appKind === 'MauiApp') {
                            if (context.state === 'closed')
                                // noinspection ExceptionCaughtLocallyJS
                                throw new Error(`${logScope}.test: AudioContext is closed.`);
                            else if (context.state === 'suspended')
                                await this.interactiveResume(context);
                        }
                        else {
                            // Skip test() call for MAUI as it doesn't exclusively use audio output or microphone;
                            // note that it will be called as part of `fix` call anyway.
                            await this.test(context, true);
                        }
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        this.markReady(context);
                        continue;
                    }
                    catch (e) {
                        warnLog?.log(`maintain: AudioContext is actually broken:`, e);
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        this.markNotReady();
                    }

                    for(;;) {
                        this.throwIfUnused();
                        this.throwIfClosed(context);
                        this.throwIfTooManyResumes();
                        try {
                            await this.fix(context);
                            break;
                        }
                        catch (e) {
                            await delayAsync(FixCyclePeriodMs);
                        }
                    }
                    this.markReady(context);
                }
            }
            catch (e) {
                warnLog?.log(`maintain: error:`, e);
            }
            finally {
                this.markNotReady();
                await this.closeSilently(context);
            }
        }
    }

    protected async create(isAlreadyInteractiveToResume = false): Promise<AudioContext> {
        debugLog?.log(`create`, isAlreadyInteractiveToResume);

        this.resumeCount = 0;
        this.interactiveResumeCount = 0;
        // Try to create audio context early w/o waiting for user interaction.
        // It might be in suspended state in this case.
        const context = new AudioContext({
            latencyHint: 'interactive',
            sampleRate: 48000,
        });
        this._contextCreated$.next(context);
        try {
            if (this.fallbackDestination)
                await this.fallbackDestination.attach(context);
            debugLog?.log(`create: loading modules`);
            const feederWorkletPath = Versioning.mapPath('/dist/feederWorklet.js');
            const encoderWorkletPath = Versioning.mapPath('/dist/opusEncoderWorklet.js');
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorklet.js');
            const whenModule1 = context.audioWorklet.addModule(feederWorkletPath);
            const whenModule2 = context.audioWorklet.addModule(encoderWorkletPath);
            const whenModule3 = context.audioWorklet.addModule(vadWorkerPath);

            if (isAlreadyInteractiveToResume) {
                debugLog?.log(`create: isAlreadyInteractiveToResume == true`);
                await this.resume(context, true);
                Interactive.isInteractive = true;
            }
            else {
                await this.interactiveResume(context);
            }

            await Promise.all([whenModule1, whenModule2, whenModule3]);
            return context;
        }
        catch (e) {
            await this.closeSilently(context);
            throw e;
        }
    }

    protected async warmup(context: AudioContext): Promise<void> {
        debugLog?.log(`warmup, AudioContext:`, Log.ref(context));

        const warmUpWorkletPath = Versioning.mapPath('/dist/warmUpWorklet.js');
        await context.audioWorklet.addModule(warmUpWorkletPath);
        const nodeOptions: AudioWorkletNodeOptions = {
            channelCount: 2,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [2],
        };
        const node = new AudioWorkletNode(context, 'warmUpWorklet', nodeOptions);
        node.connect(context.destination);

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
                whenWarmedUp.reject(`${logScope}.warmup: couldn't complete warm-up on time.`);
            })
            await whenWarmedUp;
        }
        finally {
            node.disconnect();
            node.port.onmessage = null;
            node.port.close();
        }
    }

    protected async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (context.state !== 'running')
            throw new Error(`${logScope}.test: AudioContext isn't running.`);
        if (context[Debug.brokenKey])
            throw new Error(`${logScope}.test: AudioContext is broken via .break() call.`);

        const lastTime = context.currentTime;
        const testCycleCount = 5;
        const testIntervalMs = isLongTest ? ShortTestIntervalMs : LongTestIntervalMs;
        for (let i = 0; i < testCycleCount; i++) {
            await delayAsync(testIntervalMs);
            if (context.state !== 'running')
                throw new Error(`${logScope}.test: AudioContext isn't running.`);
            if (context.currentTime != lastTime)
                break;
            // play silent audio and check state
            else if (this.isRunning(context)) {
                debugLog?.log(`test: AudioContext is running, but currentTime is not changing.`);
            }
        }
        if (context.currentTime == lastTime) // AudioContext isn't running
            throw new Error(`${logScope}.test: AudioContext is running, but didn't pass currentTime test.`);
    }

    protected async fix(context: AudioContext): Promise<void> {
        debugLog?.log(`fix:`, Log.ref(context));

        try {
            if (!await this.trySuspend(context)) {
                // noinspection ExceptionCaughtLocallyJS
                throw new Error(`${logScope}.fix: couldn't suspend AudioContext`);
            }
            await this.interactiveResume(context);
            await this.test(context);
            debugLog?.log(`fix: success`, );
        }
        catch (e) {
            warnLog?.log(`fix: failed, error:`, e);
            throw e;
        }
    }

    protected async interactiveResume(context: AudioContext): Promise<void> {
        debugLog?.log(`interactiveResume:`, Log.ref(context));
        if (context && this.isRunning(context)) {
            debugLog?.log(`interactiveResume: succeeded (AudioContext is already in running state)`);
            return;
        }

        await BrowserInfo.whenReady; // This is where isAlwaysInteractive flag gets set - it checked further
        if (Interactive.isAlwaysInteractive) {
            debugLog?.log(`interactiveResume: Interactive.isAlwaysInteractive == true`);
            await this.resume(context, false);
            await this.fallbackDestination?.play();
        }
        else {
            // Resume can be called during user interaction only
            const isWakeUp = this.isWakeUp();
            if (isWakeUp && !this.isInteractiveWasReset) {
                this.isInteractiveWasReset = true;
                Interactive.isInteractive = false;
                debugLog?.log(`interactiveResume: Interactive.isInteractive was reset on wake up`);
            }
        }

        debugLog?.log(`interactiveResume: waiting for interaction`);
        const resumeTask = new PromiseSource<boolean>();
        // Keep user gesture stack without async!!!
        const handler = Interactive.interactionEvents.add( (e) => {
            // this resume should be called without async in the same sync stack as user gesture!!!
            debugLog?.log(`interactiveResume: Interactive.interactionEvents triggered`, e);
            this.resume(context, true)
                .then(
                    () => resumeTask.resolve(true),
                    reason => {
                        warnLog?.log(reason, 'resume() failed with an error');
                        resumeTask.reject(reason);
                    });
            this.fallbackDestination?.play();
        });
        try {
            const timerTask = delayAsync(MaxInteractionWaitTimeMs).then(() => false);
            const success = await Promise.race([resumeTask, timerTask]);
            if (!success)
                throw new Error(`${logScope}.interactiveResume: timed out while waiting for interaction`);

            debugLog?.log(`interactiveResume: succeeded on interaction`);
        }
        finally {
            handler.dispose();
        }
    }

    private async resume(context: AudioContext, isInteractive: boolean): Promise<void> {
        debugLog?.log(`resume:`, Log.ref(context), isInteractive);

        this.resumeCount++;
        if (isInteractive)
            this.interactiveResumeCount++;

        if (this.isRunning(context)) {
            debugLog?.log(`resume: already resumed, AudioContext:`, Log.ref(context));
            return;
        }

        const resumeTask = context.resume().then(() => true);
        const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
        if (!await Promise.race([resumeTask, timerTask]))
            throw new Error(`${logScope}.resume: AudioContext.resume() has timed out.`);
        if (!this.isRunning(context))
            throw new Error(`${logScope}.resume: completed resume, but AudioContext.state != 'running'.`);

        debugLog?.log(`resume: resumed, AudioContext:`, Log.ref(context));
    }

    protected async trySuspend(context: AudioContext): Promise<boolean> {
        if (context.state === 'suspended') {
            debugLog?.log(`trySuspend: already suspended, AudioContext:`, Log.ref(context));
            return true;
        }
        if (context.state === 'closed') {
            debugLog?.log(`trySuspend: unable to suspend closed AudioContext`);
            return false;
        }

        debugLog?.log(`trySuspend:`, Log.ref(context));
        const suspendTask = context.suspend().then(() => true);
        const timerTask = delayAsync(MaxSuspendTimeMs).then(() => false);
        if (await Promise.race([suspendTask, timerTask])) {
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore
            if (context.state !== 'suspended') {
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


    protected isRunning(context: AudioContext): boolean {
        // This method addresses some weird issues in how AudioContext behaves in different browsers:
        // - Chromium 110 AudioContext can be in 'running' even after
        //   calling constructor, and even without user interaction.
        // - Safari doesn't start incrementing 'currentTime' after 'resume' call,
        //   so we have to warm it up w/ silent audio
        if (context.state !== 'running')
            return false;

        const silenceBuffer = context['silenceBuffer'] as AudioBuffer ?? this.createSilenceBuffer(context);
        const source = context.createBufferSource();
        source.buffer = silenceBuffer;
        source.connect(context.destination);
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        source.onended = () => source.disconnect();
        context['silenceBuffer'] = silenceBuffer;
        source.start(0);
        // Schedule to stop silence playback in the future
        source.stop(context.currentTime + SilencePlaybackDuration);
        // NOTE(AK): Somehow - sporadically - currentTime starts ticking only when you log the context!
        console.log(`AudioContext is:`, Log.ref(context), `, its currentTime:`, context.currentTime);
        return context.state === 'running';
    }

    protected async closeSilently(context?: AudioContext): Promise<void> {
        debugLog?.log(`close:`, Log.ref(context));
        if (!context)
            return;
        if (context.state === 'closed')
            return;
        if (this.fallbackDestination)
            this.fallbackDestination.detach();
        try {
            this._contextClosing$.next(context);
            await context.close();
        }
        catch (e) {
            warnLog?.log(`close: failed to close AudioContext:`, e)
        }
    }

    private createSilenceBuffer(context: AudioContext): AudioBuffer {
        return context.createBuffer(1, 1, 48000);
    }

    private throwIfUnused(): void {
        if (this._refCount == 0)
            throw new Error(`${logScope}.throwIfUnused: context is unused.`);
    }

    private throwIfTooManyResumes(): void {
        if (this.resumeCount >= MaxResumeCount)
            throw new Error(`maintain: resume attempt count is too high (${this.resumeCount}).`);
        if (this.interactiveResumeCount >= MaxInteractiveResumeCount)
            throw new Error(`maintain: interactive resume attempt count is too high (${this.interactiveResumeCount}).`);
    }

    private throwIfClosed(context: AudioContext): void {
        if (context.state === 'closed')
            throw new Error(`${logScope}.throwIfClosed: context is closed.`);
    }

    private isWakeUp(): boolean {
        return (Date.now() - this.deviceWokeUpAt) <= WakeUpDetectionIntervalMs;
    }

    // Event handlers

    private onDeviceAwake() {
        debugLog?.log(`onDeviceAwake`);
        this.deviceWokeUpAt = Date.now();
        this.isInteractiveWasReset = false;
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        void this.closeSilently(this._context);
        this.markNotReady();
    }

    private incrementRefCount(operationName: string) {
        const count = (this.refCounts.get(operationName) ?? 0) + 1;
        this.refCounts.set(operationName, count);
        this._refCount++;
        if (this._refCount > 100)
            warnLog?.log(`getRef(${operationName}): high refCount:`, this._refCount);
        debugLog?.log(`+ AudioContextRef(${operationName}), refCount: ${operationName} =`, count,  ', total =', this._refCount);
    }

    private decrementRefCount(operationName: string) {
        const count = (this.refCounts.get(operationName) ?? 0) - 1;
        if (count == 0)
            this.refCounts.delete(operationName);
        if (count < 0)
            warnLog?.log(`getRef(${operationName}): negative refCount for ${operationName}:`, count);
        this.refCounts.set(operationName, count);
        this._refCount--;
        if (this._refCount < 0)
            warnLog?.log(`getRef(${operationName}): negative refCount:`, this._refCount);
        debugLog?.log(`- AudioContextRef(${operationName}), refCount: ${operationName} =`, count, ', total =', this._refCount);
    }
}

// Init

export const audioContextSource = new AudioContextSource();
globalThis['audioContextSource'] = audioContextSource;

