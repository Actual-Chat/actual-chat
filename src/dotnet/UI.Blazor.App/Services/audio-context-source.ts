import { AUDIO_PLAY as AP, AUDIO_REC as AR } from '_constants';
import {
    Cancelled,
    debounce,
    delayAsync,
    PromiseSource,
    PromiseSourceWithTimeout,
    ResolvedPromise,
    waitAsync,
} from 'promises';
import { EventHandler } from 'event-handling';
import { Interactive } from 'interactive';
import { OnDeviceAwake } from 'on-device-awake';
import { firstValueFrom, Observable, Subject } from 'rxjs';
import { Versioning } from 'versioning';
import { BrowserInfo } from '../../UI.Blazor/Services/BrowserInfo/browser-info';
import { AudioContextRef, AudioContextRefOptions } from './audio-context-ref';
import { AudioContextDestinationFallback } from './audio-context-destination-fallback';
import { Log } from 'logging';
import { AudioInitializer, BackgroundState } from './audio-initializer';

const { logScope, infoLog, debugLog, warnLog } = Log.get('AudioContextSource');

const MaintainCyclePeriodMs = 2000;
const FixCyclePeriodMs = 300;
const MaxResumeTimeMs = 600;
const MaxResumeCount = 60;
const MaxInteractiveResumeCount = 3;
const MaxSuspendTimeMs = 300;
const ShortTestIntervalMs = 150;
const LongTestIntervalMs = 1000;
const SilencePlaybackDuration = 0.280;
const WakeUpDetectionIntervalMs = 5000;
const SuspendDebounceTimeMs: number = 2000;
const CloseUnusedContextDebounce: number = 5000;

const Debug = {
    brokenKey: 'debugging_isBroken',
}

export type AudioContextPurpose = 'recording' | 'playback';

export type OverridenAudioContext = AudioContext & {
    destinationOverride?: MediaStreamAudioDestinationNode;
};

export interface AudioContextSource {
    get context(): AudioContext;

    get refCount(): number;

    get isActive(): boolean;

    contextCreated$: Observable<AudioContext>;
    contextClosed$: Observable<AudioContext>;

    getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef;

    whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext>;

    whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void>;

    initContextInteractively(): Promise<void>;

    reset(): Promise<void>;

    updateBackgroundState(state: BackgroundState): Promise<void>;

    useRef(): void;

    pauseRef(): void;
}

abstract class AudioContextSourceBase implements AudioContextSource {
    protected readonly refs: Map<string, AudioContextRef[]> = new Map<string, AudioContextRef[]>();
    protected readonly fallbackDestination?: AudioContextDestinationFallback = null;
    protected readonly _contextCreated$: Subject<AudioContext> = new Subject<AudioContext>();
    protected readonly _contextClosed$: Subject<AudioContext> = new Subject<AudioContext>();

    protected onDeviceAwakeHandler: EventHandler<number>;
    protected _context: OverridenAudioContext | null = null;
    protected _refCount = 0;

    public readonly contextCreated$: Observable<AudioContext> = this._contextCreated$.asObservable();
    public readonly contextClosed$: Observable<AudioContext> = this._contextClosed$.asObservable();

    // Key properties
    public get context(): OverridenAudioContext { return this._context; }
    public get refCount(): number { return this._refCount }
    public abstract get isActive(): boolean;

    public get hasRefsInUse(): boolean {
        for (let [_, refs] of this.refs) {
            for (let ref of refs) {
                if (ref.isUsed)
                    return true;
            }
        }
        return false;
    }

    protected constructor(public readonly purpose: AudioContextPurpose) {
        this.onDeviceAwakeHandler = OnDeviceAwake.events.add(() => this.onDeviceAwake());
        if (purpose === 'playback') {
            if (AudioContextDestinationFallback.isRequired)
                this.fallbackDestination = new AudioContextDestinationFallback();

            if ('mediaSession' in navigator) {
                navigator.mediaSession.metadata = new MediaMetadata({
                    title: `Ready`,
                    artist: 'Actual Chat',
                    artwork: [{ src: '/_applogo-dark.svg' }]
                });
                navigator.mediaSession.playbackState = 'none';
                navigator.mediaSession.setPositionState({
                    playbackRate: 1,
                    position: 0,
                    duration: 0,
                });
            }
            if ('audioSession' in navigator) {
                navigator.audioSession['type'] = 'playback'; // 'playback'
            }
        }
        else {
            if ('audioSession' in navigator) {
                navigator.audioSession['type'] = 'play-and-record'; // 'recording'
            }
        }
    }

    public getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef {
        const result = new AudioContextRef(this, operationName, options);
        this.registerRef(operationName, result);

        void result.whenDisposed().then(() => this.unregisterRef(operationName, result));
        return result;
    }

    public abstract whenReady(cancel?: Promise<symbol>): Promise<AudioContext>;

    public abstract whenNotReady(context: AudioContext, cancel?: Promise<symbol>): Promise<void>;

    public abstract initContextInteractively(): Promise<void>;

    public abstract reset(): Promise<void>;

    public abstract updateBackgroundState(state: BackgroundState): Promise<void>;

    public abstract useRef(): void;

    public abstract pauseRef(): void;

    protected async loadContextWorklets(context: AudioContext): Promise<void> {
        try {
            debugLog?.log(`loadContextWorklets: loading modules`);
            const feederWorkletPath = Versioning.mapPath('/dist/feederWorklet.js');
            const encoderWorkletPath = Versioning.mapPath('/dist/opusEncoderWorklet.js');
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorklet.js');
            const whenModule1 = context.audioWorklet.addModule(feederWorkletPath);
            const whenModule2 = context.audioWorklet.addModule(encoderWorkletPath);
            const whenModule3 = context.audioWorklet.addModule(vadWorkerPath);
            await Promise.all([whenModule1, whenModule2, whenModule3]);
        }
        catch (e) {
            warnLog?.log(`loadContextWorklets: failed to load modules:`, e);
            await this.closeSilently(context);
            throw e;
        }
    }

    protected async closeSilently(context?: AudioContext): Promise<void> {
        debugLog?.log(`close:`, Log.ref(context));
        if (!context)
            return;

        if (context.state === 'closed')
            return;

        this.fallbackDestination?.detach();
        try {
            await context.close();
        }
        catch (e) {
            warnLog?.log(`close: failed to close AudioContext:`, e)
        }
        finally {
            this._contextClosed$.next(context);
        }
    }

    // Event handlers

    protected abstract onDeviceAwake(): Promise<void>;

    private registerRef(operationName: string, ref: AudioContextRef) {
        const { refs } = this;
        const opRefs = refs.get(operationName);
        if (opRefs)
            opRefs.push(ref);
        else
            refs.set(operationName, [ref]);
        const count = (opRefs?.length ?? 0) + 1;
        this._refCount++;
        if (this._refCount > 100)
            warnLog?.log(`getRef(${operationName}): high refCount:`, this._refCount);
        debugLog?.log(`+ AudioContextRef(${operationName}), refCount: ${operationName} =`, count,  ', total =', this._refCount);
    }

    private unregisterRef(operationName: string, ref: AudioContextRef) {
        const { refs } = this;
        const opRefs = refs.get(operationName);
        const count = (opRefs?.length ?? 0) - 1;
        if (count == 0)
            this.refs.delete(operationName);
        else if (opRefs) {
            const index = opRefs.indexOf(ref);
            if (index > -1) {
                opRefs.splice(index, 1);
            }
        }
        if (count < 0)
            warnLog?.log(`getRef(${operationName}): negative refCount for ${operationName}:`, count);
        this._refCount--;
        if (this._refCount < 0)
            warnLog?.log(`getRef(${operationName}): negative refCount:`, this._refCount);
        debugLog?.log(`- AudioContextRef(${operationName}), refCount: ${operationName} =`, count, ', total =', this._refCount);
    }
}

class WebAudioContextSource extends AudioContextSourceBase implements AudioContextSource {
    private _isActive = false;
    private deviceWokeUpAt = 0;
    private isInteractiveWasReset = false;
    private resumeCount = 0;
    private interactiveResumeCount = 0;
    private _maintain: Promise<void> | null = null;
    private _whenReady = new PromiseSource<AudioContext | null>();
    private _whenNotReady = new PromiseSource<void>();

    // Key properties
    public get isActive(): boolean { return this._isActive }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
        this._maintain = this.maintain();
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
        Interactive.isInteractive = true;
        debugLog?.log(`initContextInteractively()`);

        if (this._context && this._context.state === 'running') {
            debugLog?.log(`initContextInteractively: already running`);
            return; // Already ready
        } else if (this._context && this._context.state === 'suspended') {
            await this._context.resume();
            return;
        }

        const context = await this.create(true);
        this.markReady(context);
    }

    public async reset(): Promise<void> {
        this._isActive = false;
        await this.closeSilently(this._context);
        this.markNotReady();
        if (this._maintain)
            await this._maintain;
        this._maintain = this.maintain();
    }

    public async updateBackgroundState(state: BackgroundState): Promise<void> {
        debugLog?.log(`updateBackgroundState:`, state, this._isActive);
        // if (state === 'BackgroundIdle') {
        //     this._isActive = false;
        //     this.markNotReady();
        //     return;
        // }
        //
        // if (!this._isActive) {
        //     if (this._maintain)
        //         await this._maintain;
        //     this._maintain = this.maintain();
        // }
    }

    public pauseRef(): void { }

    public useRef(): void {
        if (!this._isActive) {
            this._maintain = this.maintain();
            return;
        }
        const context = this._context;
        if (context && context.state !== 'running') {
            warnLog?.log('useRef: context is not running', context.state);
        }
    }

    // Must be private, but good to keep it near markNotReady
    private markReady(context: AudioContext | null) {
        // Invariant it maintains on exit:
        // - _context != null
        // - _whenReady is completed
        // - _whenNotReady is NOT completed.

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
        debugLog?.log('maintain: starting');
        this._isActive = true;
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        await delayAsync(300);
        // noinspection InfiniteLoopJS
        let retryCount = 0;
        while (this._isActive) { // Renew loop
            debugLog?.log('maintain: loop 1');
            let context = await this.create();
            this.markReady(context);
            if (context.state === 'suspended') {
                // Wait for the next user interaction to resume the context
                const whenInteractive = firstValueFrom(Interactive.interactionEvent$);
                await Promise.race([this._whenNotReady, whenInteractive]);
            }
            try {
                let lastTestAt = Date.now();

                // noinspection InfiniteLoopJS
                while (this._isActive) { // Fix loop
                    debugLog?.log('maintain: loop 2');
                    const minDelay = lastTestAt + MaintainCyclePeriodMs - Date.now();
                    if (minDelay > 0) {
                        await delayAsync(minDelay);
                    }
                    else {
                        const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                        await Promise.race([this._whenNotReady, whenDelayCompleted]);
                    }

                    if (!this._isActive)
                        break;

                    // Let's try to test whether AudioContext is broken and fix if it is in use by any audioContextRef
                    if (!this.hasRefsInUse || !Interactive.isInteractive) {
                        // Wait for the next user interaction as refs can appear after some user interactions
                        const whenInteractive = firstValueFrom(Interactive.interactionEvent$);
                        await Promise.race([this._whenNotReady, whenInteractive]);
                        continue;
                    }

                    try {
                        lastTestAt = Date.now();
                        await this.test(context, true);
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        retryCount = 0;
                        this.markReady(context);
                        continue;
                    }
                    catch (e) {
                        warnLog?.log(`maintain: AudioContext is actually broken:`, e);
                        // See the description of markReady/markNotReady to understand the invariant it maintains
                        this.markNotReady();
                    }

                    while (this._isActive) {
                        debugLog?.log('maintain: loop 3');
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
                    retryCount = 0;
                    this.markReady(context);
                }
            }
            catch (e) {
                warnLog?.log(`maintain: error:`, e);
                if (retryCount++ > 1) {
                    // wait for the next user interaction to prevent creating broken AudioContexts
                    warnLog?.log(`maintain: waiting for user interaction...`);
                    await firstValueFrom(Interactive.interactionEvent$);
                    warnLog?.log(`maintain: resuming maintain cycle`);
                }
            }
            finally {
                this.markNotReady();
                await this.closeSilently(context);
            }
        }
        if (this._context?.state !== 'closed')
            await this.closeSilently(this._context);
    }

    private async create(isAlreadyInteractiveToResume = false): Promise<AudioContext> {
        debugLog?.log(`create`, isAlreadyInteractiveToResume);

        this.resumeCount = 0;
        this.interactiveResumeCount = 0;
        // Try to create audio context early w/o waiting for user interaction.
        // It might be in suspended state in this case.
        const context: OverridenAudioContext = new AudioContext({
            latencyHint: 'balanced',
            sampleRate: this.purpose === 'playback' ? AP.SAMPLE_RATE : AR.SAMPLE_RATE,
        });
        this._contextCreated$.next(context);
        try {
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
                void this.interactiveResume(context);
            }

            await Promise.all([whenModule1, whenModule2, whenModule3]);
            if (this.fallbackDestination) {
                context.destinationOverride = this.fallbackDestination.destination;
                await this.fallbackDestination.attach(context);
            }

            return context;
        }
        catch (e) {
            warnLog?.log('create: failed to create', e);
            await this.closeSilently(context);
            throw e;
        }
    }

    private async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (!this._isActive)
            return;

        if (context.state !== 'running')
            throw new Error(`${logScope}.test: AudioContext isn't running.`);
        if (context[Debug.brokenKey])
            throw new Error(`${logScope}.test: AudioContext is broken via .break() call.`);

        const lastTime = context.currentTime;
        const testCycleCount = 5;
        const testIntervalMs = isLongTest ? LongTestIntervalMs : ShortTestIntervalMs;
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

    private async fix(context: AudioContext): Promise<void> {
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

    private async interactiveResume(context: AudioContext): Promise<void> {
        debugLog?.log(`interactiveResume:`, Log.ref(context));
        if (context && this.isRunning(context)) {
            debugLog?.log(`interactiveResume: succeeded (AudioContext is already in running state)`);
            await this.fallbackDestination?.play();
            return;
        }

        if (!Interactive.isAlwaysInteractive)
            await BrowserInfo.whenReady; // This is where isAlwaysInteractive flag gets set - it checked further
        if (Interactive.isAlwaysInteractive) {
            debugLog?.log(`interactiveResume: Interactive.isAlwaysInteractive == true`);
            await this.resume(context, false);
            Interactive.isInteractive = true;
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
        });
        try {
            await resumeTask;
            Interactive.isInteractive = true;
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

        void this.fallbackDestination?.play();
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

    private async trySuspend(context: AudioContext): Promise<boolean> {
        this.fallbackDestination?.pause();
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


    private isRunning(context: AudioContext): boolean {
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

    private createSilenceBuffer(context: AudioContext): AudioBuffer {
        return context.createBuffer(1, 1, this.purpose === 'playback' ? AP.SAMPLE_RATE : AR.SAMPLE_RATE);
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

    protected async onDeviceAwake(): Promise<void> {
        debugLog?.log(`onDeviceAwake`);
        this.deviceWokeUpAt = Date.now();
        this.isInteractiveWasReset = false;
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        await this.closeSilently(this._context);
        this.markNotReady();
    }
}

class MauiAudioContextSource extends AudioContextSourceBase implements AudioContextSource {
    private whileBackgroundIdle: PromiseSource<void> | null = null;

    get isActive(): boolean { return true; }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
    }

    public async whenReady(cancel?: Promise<symbol>): Promise<AudioContext> {
        const context = this._context;
        if (context == null || context.state === 'closed') {
            const whileBackgroundIdle = this.whileBackgroundIdle;
            if (whileBackgroundIdle)
                await whileBackgroundIdle;
            this._context = await this.create();
        }
        return this._context;
    }

    public async whenNotReady(context: AudioContext, cancel?: Promise<symbol>): Promise<void> {
        const whenContextClosed = firstValueFrom(this.contextClosed$);
        await waitAsync(whenContextClosed, cancel);
    }

    public initContextInteractively(): Promise<void> {
        return Promise.resolve();
    }

    public async reset(): Promise<void> {
        await this.closeContext();
    }

    public async updateBackgroundState(state: BackgroundState): Promise<void> {
        debugLog?.log(`updateBackgroundState:`, state, this.hasRefsInUse);
        if (state === 'BackgroundIdle') {
            if (!this.whileBackgroundIdle)
                this.whileBackgroundIdle = new PromiseSource<void>();
            if (!this.hasRefsInUse)
                this.suspendContextDebounced();
        }
        else {
            this.whileBackgroundIdle?.resolve(undefined);
            this.whileBackgroundIdle = null;
        }
    }

    public pauseRef(): void {
        const hasRefsInUse = this.hasRefsInUse;
        const backgroundState = AudioInitializer.backgroundState;
        infoLog?.log('pauseRef:', hasRefsInUse, backgroundState);
        if (!hasRefsInUse) {
            this.suspendContextDebounced();
            if (backgroundState === 'BackgroundIdle')
                this.closeContextDebounced();
        }
    }

    public useRef(): void {
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();
        const context = this._context;
        if (!context || context.state === 'closed')
            void this.whenReady();
        else if (context.state === 'suspended') {
            void context.resume().then(async () => {
                infoLog?.log('useRef: resume context');
                await this.fallbackDestination?.play();
            });
        }
    }

    private suspendContextDebounced = debounce(this.suspendContext, SuspendDebounceTimeMs);
    private async suspendContext(): Promise<void> {
        infoLog?.log('suspendContext()');
        const context = this._context;
        if (!context)
            return;

        if (context.state === 'closed') {
            await this.closeContext();
            return;
        }

        await context.suspend();
        this.fallbackDestination?.pause();
        if (AudioInitializer.backgroundState === 'BackgroundIdle')
            this.closeContextDebounced();
    }

    private closeContextDebounced = debounce(() => this.closeContext(), CloseUnusedContextDebounce);
    private async closeContext(): Promise<void> {
        infoLog?.log('closeContext()');
        const context = this._context;
        this._context = null;
        await this.closeSilently(context);
        if (AudioInitializer.backgroundState !== 'BackgroundIdle') {
            this.whileBackgroundIdle?.resolve(undefined);
            this.whileBackgroundIdle = null;
        }
    }

    private async create(): Promise<AudioContext> {
        debugLog?.log(`create`);
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();
        const context = new AudioContext({
            latencyHint: 'balanced',
            sampleRate: this.purpose === 'playback' ? AP.SAMPLE_RATE : AR.SAMPLE_RATE,
        });
        Interactive.isInteractive = true;
        await this.fallbackDestination?.attach(context);
        await this.loadContextWorklets(context);

        this._contextCreated$.next(context);
        return context;
    }

    protected async onDeviceAwake(): Promise<void> {
        debugLog?.log(`onDeviceAwake`);
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        await this.closeContext();
    }
}


// Init

export const audioContextSource: AudioContextSource = BrowserInfo.hostKind === "MauiApp"
    ? new MauiAudioContextSource('playback')
    : new WebAudioContextSource('playback');
globalThis['audioContextSource'] = audioContextSource;

export const recordingAudioContextSource: AudioContextSource = BrowserInfo.hostKind === "MauiApp"
    ? new MauiAudioContextSource('recording')
    : new WebAudioContextSource('recording');
globalThis['recordingAudioContextSource'] = recordingAudioContextSource;
