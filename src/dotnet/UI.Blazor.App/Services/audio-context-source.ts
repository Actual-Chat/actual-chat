import { AUDIO_PLAY as AP, AUDIO_REC as AR } from '_constants';
import {
    Cancelled,
    debounce,
    delayAsync,
    PromiseSource,
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
import { Disposable, Disposables } from 'disposable';

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
    get isMaintained(): boolean;
    get isContextRunning(): boolean;

    contextCreated$: Observable<AudioContext>;
    contextClosed$: Observable<AudioContext>;

    getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef;

    useRef(ref: AudioContextRef): Disposable;

    whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext>;

    whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void>;

    initContextInteractively(): Promise<void>;

    reset(): Promise<void>;

    updateBackgroundState(state: BackgroundState): Promise<void>;
}

class AudioContextSource_ implements AudioContextSource {
    private readonly refs: Map<string, AudioContextRef[]> = new Map<string, AudioContextRef[]>();
    private readonly fallbackDestination?: AudioContextDestinationFallback = null;
    private readonly _contextCreated$: Subject<AudioContext> = new Subject<AudioContext>();
    private readonly _contextClosed$: Subject<AudioContext> = new Subject<AudioContext>();

    private whileBackgroundIdle: PromiseSource<void> | null = null;
    private onDeviceAwakeHandler: EventHandler<number>;
    private context: OverridenAudioContext | null = null;
    private refCount = 0;
    private isActive: boolean;
    private _maintain: Promise<void> | null = null;
    private _whenReady = new PromiseSource<AudioContext | null>();
    private _whenNotReady = new PromiseSource<void>();
    private deviceWokeUpAt = 0;
    private isInteractiveWasReset = false;
    private resumeCount = 0;
    private interactiveResumeCount = 0;

    public readonly contextCreated$: Observable<AudioContext> = this._contextCreated$.asObservable();
    public readonly contextClosed$: Observable<AudioContext> = this._contextClosed$.asObservable();

    // Key properties
    public get isContextRunning(): boolean { return this.context && this.context.state === 'running'; }
    public get isMaintained(): boolean { return this.isActive }

    public get hasRefsInUse(): boolean {
        for (let [_, refs] of this.refs) {
            for (let ref of refs) {
                if (ref.isUsed)
                    return true;
            }
        }
        return false;
    }

    public constructor(public readonly purpose: AudioContextPurpose, public readonly isMaui: boolean) {
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
        this._maintain = this.maintain();
    }

    public getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef {
        const result = new AudioContextRef(this, operationName, options);
        this.registerRef(operationName, result);

        void result.whenDisposed().then(() => this.unregisterRef(operationName, result));
        return result;
    }

    public useRef(ref: AudioContextRef): Disposable {
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();

        // Reset waiting for active state
        this.whileBackgroundIdle?.resolve(undefined);
        this.whileBackgroundIdle = null;
        if (!this.isActive)
            this._maintain = this.maintain();
        const context = this.context;
        if (context && context.state !== 'running') {
            if (this.isMaui && context.state === 'suspended') {
                void context.resume().then(async () => {
                    infoLog?.log('useRef: resume context');
                    await this.fallbackDestination?.play();
                });
            }
            else {
                warnLog?.log('useRef: context is not running', context.state);
                this.markNotReady();
            }
        }

        return this.isMaui
            ? Disposables.fromAction(() => {
                const hasRefsInUse = this.hasRefsInUse;
                const backgroundState = AudioInitializer.backgroundState;
                infoLog?.log('pauseRef:', hasRefsInUse, backgroundState);
                if (hasRefsInUse)
                    return;

                this.suspendContextDebounced();
                if (backgroundState === 'BackgroundIdle')
                    this.closeContextDebounced();
            })
            : Disposables.empty();
    }

    public whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext> {
        return waitAsync(this._whenReady, cancel);
    }

    public whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void> {
        if (!context || this.context != context)
            return ResolvedPromise.Void;

        return waitAsync(this._whenNotReady, cancel);
    }

    public async initContextInteractively(): Promise<void> {
        Interactive.isInteractive = true;
        debugLog?.log(`initContextInteractively()`);

        let context = this.context;
        if (context && context.state === 'running') {
            debugLog?.log(`initContextInteractively: already running`);
            return; // Already ready
        } else if (context && context.state === 'suspended') {
            await context.resume();
            return;
        }

        context = await this.create(true);
        this.markReady(context);
    }

    public async reset(): Promise<void> {
        this.isActive = false;
        await this.closeSilently(this.context);
        this.markNotReady();
        if (this._maintain)
            await this._maintain;
        this._maintain = this.maintain();
    }

    public async updateBackgroundState(state: BackgroundState): Promise<void> {
        debugLog?.log(`updateBackgroundState:`, state, this.isActive, this.hasRefsInUse);
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

    // Private methods

    protected async maintain(): Promise<void> {
        debugLog?.log('maintain: starting');
        this.isActive = true;
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        await delayAsync(300);
        // noinspection InfiniteLoopJS
        let retryCount = 0;
        while (this.isActive) { // Renew loop
            debugLog?.log('maintain: loop 1');
            let context = await this.create();
            this.markReady(context);
            if (context.state === 'suspended') {
                if (this.isMaui) {
                    await context.resume();
                    await this.fallbackDestination?.play();
                }
                else {
                    // Wait for the next user interaction to resume the context
                    const whenInteractive = firstValueFrom(Interactive.interactionEvent$);
                    await Promise.race([this._whenNotReady, whenInteractive]);
                }
            }
            try {
                let lastTestAt = Date.now();

                // noinspection InfiniteLoopJS
                while (this.isActive) { // Fix loop
                    debugLog?.log('maintain: loop 2');
                    const minDelay = lastTestAt + MaintainCyclePeriodMs - Date.now();
                    if (minDelay > 0) {
                        await delayAsync(minDelay);
                    }
                    else {
                        const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                        await Promise.race([this._whenNotReady, whenDelayCompleted]);
                    }

                    if (!this.isActive)
                        break;

                    // Let's try to test whether AudioContext is broken and fix if it is in use by any audioContextRef
                    if (!this.hasRefsInUse || !Interactive.isInteractive) {
                        // Wait for the next user interaction as refs can appear after some user interactions
                        const whenInteractive = firstValueFrom(Interactive.interactionEvent$);
                        const whenNotReady = this._whenNotReady;
                        const whileBackgroundIdle = this.whileBackgroundIdle;
                        if (whileBackgroundIdle && !whileBackgroundIdle.isCompleted())
                            await Promise.race([whenNotReady, whenInteractive, whileBackgroundIdle]);
                        else
                            await Promise.race([whenNotReady, whenInteractive]);
                        if (!whenNotReady.isCompleted()) {
                            if (!whileBackgroundIdle || !whileBackgroundIdle.isCompleted())
                                await this.resume(context, true); // Resume context as we get interactive event callback
                            continue; // Go to the test/fix below when not ready
                        }
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

                    while (this.isActive) {
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
        if (this.context?.state !== 'closed')
            await this.closeSilently(this.context);
    }

    private markReady(context: AudioContext | null) {
        // Invariant it maintains on exit:
        // - _context != null
        // - _whenReady is completed
        // - _whenNotReady is NOT completed.

        if (this.context)
            return; // Already ready

        this.context = context;
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

        const audioContext = this.context;
        if (!audioContext)
            return; // Already not ready

        this.context = null;
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


    private async create(isAlreadyInteractiveToResume = this.isMaui): Promise<AudioContext> {
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
            const whenWorkletsLoaded = this.loadContextWorklets(context)
            if (isAlreadyInteractiveToResume) {
                debugLog?.log(`create: isAlreadyInteractiveToResume == true`);
                await this.resume(context, true);
                Interactive.isInteractive = true;
            }
            else {
                void this.interactiveResume(context);
            }

            await whenWorkletsLoaded;
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

    private async loadContextWorklets(context: AudioContext): Promise<void> {
        try {
            debugLog?.log(`loadContextWorklets: loading modules`);
            const feederWorkletPath = Versioning.mapPath('/dist/feederWorklet.js');
            const encoderWorkletPath = Versioning.mapPath('/dist/opusEncoderWorklet.js');
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorklet.js');
            if (this.purpose === 'playback')
                await context.audioWorklet.addModule(feederWorkletPath);
            else {
                const whenModule1 = context.audioWorklet.addModule(encoderWorkletPath);
                const whenModule2 = context.audioWorklet.addModule(vadWorkerPath);
                await Promise.all([whenModule1, whenModule2]);
            }

        }
        catch (e) {
            warnLog?.log(`loadContextWorklets: failed to load modules:`, e);
            await this.closeSilently(context);
            throw e;
        }
    }

    private async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (!this.isActive)
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

    private async closeSilently(context?: AudioContext): Promise<void> {
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

    private suspendContextDebounced = debounce(this.suspendContext, SuspendDebounceTimeMs);
    private async suspendContext(): Promise<void> {
        infoLog?.log('suspendContext()');
        const context = this.context;
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
        const context = this.context;
        this.context = null;
        await this.closeSilently(context);
        if (AudioInitializer.backgroundState !== 'BackgroundIdle') {
            this.whileBackgroundIdle?.resolve(undefined);
            this.whileBackgroundIdle = null;
        }
    }

    private async onDeviceAwake(): Promise<void> {
        debugLog?.log(`onDeviceAwake`);
        this.deviceWokeUpAt = Date.now();
        this.isInteractiveWasReset = false;
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        await this.closeSilently(this.context);
        this.markNotReady();
    }

    private registerRef(operationName: string, ref: AudioContextRef) {
        const { refs } = this;
        const opRefs = refs.get(operationName);
        if (opRefs)
            opRefs.push(ref);
        else
            refs.set(operationName, [ref]);
        const count = (opRefs?.length ?? 0) + 1;
        this.refCount++;
        if (this.refCount > 100)
            warnLog?.log(`getRef(${operationName}): high refCount:`, this.refCount);
        debugLog?.log(`+ AudioContextRef(${operationName}), refCount: ${operationName} =`, count,  ', total =', this.refCount);
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
        this.refCount--;
        if (this.refCount < 0)
            warnLog?.log(`getRef(${operationName}): negative refCount:`, this.refCount);
        debugLog?.log(`- AudioContextRef(${operationName}), refCount: ${operationName} =`, count, ', total =', this.refCount);
    }
}

// Init

export const audioContextSource: AudioContextSource = BrowserInfo.hostKind === "MauiApp"
    ? new AudioContextSource_('playback', true)
    : new AudioContextSource_('playback', false);
globalThis['audioContextSource'] = audioContextSource;

export const recordingAudioContextSource: AudioContextSource = BrowserInfo.hostKind === "MauiApp"
    ? new AudioContextSource_('recording', true)
    : new AudioContextSource_('recording', false);
globalThis['recordingAudioContextSource'] = recordingAudioContextSource;
