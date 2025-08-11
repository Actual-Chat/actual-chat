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
import { DeviceInfo } from 'device-info';

const { logScope, infoLog, debugLog, warnLog } = Log.get('AudioContextSource');

const MaintainCyclePeriodMs = 2000;
const FixCyclePeriodMs = 300;
const MaxResumeTimeMs = 1500;
const MaxResumeCount = 60;
const MaxInteractiveResumeCount = 3;
const MaxSuspendTimeMs = 600;
const ShortTestIntervalMs = 150;
const LongTestIntervalMs = 1000;
const SilencePlaybackDuration = 0.280;
const WakeUpDetectionIntervalMs = 5000;
const SuspendDebounceTimeMs: number = 2000;
const CloseUnusedContextDebounce: number = 60000;

const Debug = {
    brokenKey: 'debugging_isBroken',
}

export type AudioContextPurpose = 'recording' | 'playback';

export type OverridenAudioContext = AudioContext & {
    wasInteractive?: boolean;
    destination_?: MediaStreamAudioDestinationNode;
    destinationFallback?: AudioContextDestinationFallback;
};

export function resetMediaSessionMetadata(): void {
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
}

export const resetMediaSessionDebounced = debounce(() => resetMediaSessionMetadata(), AP.MEDIA_SESSION_RESET_DEBOUNCE_MS);

export interface AudioContextSource {
    get isMaintained(): boolean;
    get isContextRunning(): boolean;

    contextCreated$: Observable<AudioContext>;
    contextClosed$: Observable<AudioContext>;

    getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef;

    useRef(ref: AudioContextRef): Promise<Disposable>;

    whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext>;

    whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void>;

    initContextInteractively(): Promise<void>;

    reset(): Promise<void>;

    updateBackgroundState(state: BackgroundState): Promise<void>;

    interactiveResume(context: OverridenAudioContext): Promise<void>;
}

abstract class AudioContextSourceBase implements AudioContextSource {
    protected readonly refs: Map<string, AudioContextRef[]> = new Map<string, AudioContextRef[]>();
    protected readonly _contextCreated$: Subject<AudioContext> = new Subject<AudioContext>();
    protected readonly _contextClosed$: Subject<AudioContext> = new Subject<AudioContext>();

    protected deviceWokeUpAt = 0;
    protected isInteractiveWasReset = false;
    protected resumeCount = 0;
    protected interactiveResumeCount = 0;
    protected whileBackgroundIdle: PromiseSource<void> | null = null;
    protected onDeviceAwakeHandler: EventHandler<number>;
    protected _context: OverridenAudioContext | null = null;
    protected _refCount = 0;

    public readonly contextCreated$: Observable<AudioContext> = this._contextCreated$.asObservable();
    public readonly contextClosed$: Observable<AudioContext> = this._contextClosed$.asObservable();

    // Key properties
    public get isContextRunning(): boolean { return !!this._context && this._context.state === 'running'; }
    public abstract get isMaintained(): boolean;

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
        this.onDeviceAwakeHandler = OnDeviceAwake.events.add(durationMs => this.onDeviceAwake(durationMs));
        if (purpose === 'playback') {
            if ('audioSession' in navigator && typeof navigator.audioSession === 'object') {
                (navigator.audioSession as any)['type'] = 'playback';
                (navigator.audioSession as any)['type'] = 'auto'; // Hack for iOS Safari
                (navigator.audioSession as any)['type'] = 'playback';
            }
            resetMediaSessionMetadata();
        }

    }

    public getRef(operationName: string, options: AudioContextRefOptions): AudioContextRef {
        const result = new AudioContextRef(this, operationName, options);
        this.registerRef(operationName, result);

        void result.whenDisposed().then(() => this.unregisterRef(operationName, result));
        return result;
    }

    public abstract useRef(ref: AudioContextRef): Promise<Disposable>;

    public abstract whenReady(cancel?: Promise<symbol>): Promise<AudioContext>;

    public abstract whenNotReady(context: AudioContext, cancel?: Promise<symbol>): Promise<void>;

    public abstract initContextInteractively(): Promise<void>;

    public abstract reset(): Promise<void>;

    public async updateBackgroundState(state: BackgroundState): Promise<void> {
        debugLog?.log(`updateBackgroundState:`, state, this.hasRefsInUse);
        if (state === 'BackgroundIdle') {
            if (!this.whileBackgroundIdle)
                this.whileBackgroundIdle = new PromiseSource<void>();
            if (!this.hasRefsInUse)
                this.suspendContextDebounced();
        }
        else {
            this.suspendContextDebounced.reset();
            this.closeContextDebounced.reset();
            this.whileBackgroundIdle?.resolve(undefined);
            this.whileBackgroundIdle = null;
        }
    }

    public async interactiveResume(context: OverridenAudioContext): Promise<void> {
        debugLog?.log(`interactiveResume:`, Log.ref(context));
        if (context && this.isRunning(context)) {
            debugLog?.log(`interactiveResume: succeeded (AudioContext is already in running state)`);
            Interactive.isInteractive = true;
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
            const currentContext = this._context;
            let contextToResume = context;
            if (currentContext && currentContext !== context) {
                warnLog?.log('interactiveResume: context has already been changed, will try to use the new one');
                contextToResume = currentContext;
            }
            if (contextToResume.state === 'closed') {
                warnLog?.log('interactiveResume: context is closed, will try to create a new one');
                this.create(true)
                    .then(
                        () => resumeTask.resolve(true),
                        reason => {
                            warnLog?.log(reason, 'create(true) failed with an error');
                            resumeTask.reject(reason);
                        });
                return;
            }
            this.resume(contextToResume, true)
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

    // Protected methods

    protected async create(shouldResume: boolean = false): Promise<AudioContext> {
        debugLog?.log(`create`);
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();
        this.resumeCount = 0;
        this.interactiveResumeCount = 0;
        // Try to create audio context early w/o waiting for user interaction.
        // It might be in suspended state in this case.
        const context: OverridenAudioContext = new AudioContext({
            latencyHint: 'balanced',
            sampleRate: this.purpose === 'playback'
                ? AP.SAMPLE_RATE
                : DeviceInfo.isFirefox ? undefined : AR.SAMPLE_RATE, // FF doesn't support sample rate for microphone stream, we will use default
        });

        if (shouldResume)
            await this.resume(context, true);
        try {
            debugLog?.log(`create: loading modules`);
            const whenWorkletsLoaded =  this.loadContextWorklets(context);

            if (!Interactive.isAlwaysInteractive && !shouldResume)
                await this.interactiveResume(context);

            await whenWorkletsLoaded;
            if (this.purpose === 'playback' && AudioContextDestinationFallback.isRequired) {
                const destinationFallback = new AudioContextDestinationFallback(context);
                context.destination_ = destinationFallback.destination;
                context.destinationFallback = destinationFallback;
            }
            this._contextCreated$.next(context);

            return context;
        }
        catch (e) {
            warnLog?.log('create: failed to create', e);
            await this.closeSilently(context);
            throw e;
        }
    }

    protected async resume(context: OverridenAudioContext, isInteractive: boolean): Promise<void> {
        debugLog?.log(`resume:`, Log.ref(context), isInteractive);

        const resumeTask = context.resume().then(() => true);
        this.resumeCount++;
        if (isInteractive)
            this.interactiveResumeCount++;

        if (this.isRunning(context)) {
            debugLog?.log(`resume: already resumed, AudioContext:`, Log.ref(context));
            context.wasInteractive = true;
            Interactive.isInteractive = true;
            return;
        }

        const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
        if (!await Promise.race([resumeTask, timerTask]))
            throw new Error(`${logScope}.resume: AudioContext.resume() has timed out.`);
        if (!this.isRunning(context))
            throw new Error(`${logScope}.resume: completed resume, but AudioContext.state != 'running'.`);

        context.wasInteractive = true;
        Interactive.isInteractive = true;
        debugLog?.log(`resume: resumed, AudioContext:`, Log.ref(context));
    }

    protected async loadContextWorklets(context: AudioContext): Promise<void> {
        try {
            debugLog?.log(`loadContextWorklets: loading modules`);
            if (this.purpose === 'playback') {
                const feederWorkletPath = Versioning.mapPath('/dist/feederWorklet.js');
                await context.audioWorklet.addModule(feederWorkletPath);
            }
            else {
                const vadWorkletPath = Versioning.mapPath('/dist/vadWorklet.js');
                const encoderWorkletPath = Versioning.mapPath('/dist/opusEncoderWorklet.js');
                const whenModule1 = context.audioWorklet.addModule(vadWorkletPath);
                const whenModule2 = context.audioWorklet.addModule(encoderWorkletPath);
                await Promise.all([whenModule1, whenModule2]);
            }
        }
        catch (e) {
            warnLog?.log(`loadContextWorklets: failed to load modules:`, e);
            await this.closeSilently(context);
            throw e;
        }
    }

    protected isRunning(context: OverridenAudioContext): boolean {
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
        const destinationOverride = context.destination_ ?? context.destination;
        source.connect(destinationOverride);
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        source.onended = () => source.disconnect();
        context['silenceBuffer'] = silenceBuffer;
        source.start(0);
        // Schedule to stop silence playback in the future
        source.stop(context.currentTime + SilencePlaybackDuration);
        // NOTE(AK): Somehow - sporadically - currentTime starts ticking only when you log the context!
        console.log(`AudioContext is:`, Log.ref(context), `, its currentTime:`, context.currentTime);
        const isRunning = context.state === 'running';
        if (isRunning) {
            // Resume call was successful
            this.resumeCount = 0;
            this.interactiveResumeCount = 0;
            context[Debug.brokenKey] = undefined;
        }
        return isRunning;
    }

    protected async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (context.state !== 'running')
            throw new Error(`${logScope}.test: AudioContext isn't running.`);
        if (context[Debug.brokenKey])
            throw new Error(`${logScope}.test: AudioContext is broken via .break() call.`);

        const lastTime = context.currentTime;
        const testCycleCount = 5;
        const testIntervalMs = isLongTest ? LongTestIntervalMs : ShortTestIntervalMs;
        for (let i = 0; i < testCycleCount; i++) {
            if (!this.testPlayback(context))
                throw new Error(`${logScope}.test: playback test failed.`);
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

    private testPlayback(context: AudioContext): boolean {
        try {
            const oscillator = context.createOscillator();
            const gainNode = context.createGain();

            gainNode.gain.setValueAtTime(0.001, context.currentTime); // Very low volume

            // Connect oscillator -> gain -> destination
            oscillator.connect(gainNode);
            gainNode.connect(context.destination);

            // Use a very high frequency (less audible)
            oscillator.frequency.setValueAtTime(20000, context.currentTime); // 20kHz - near upper limit of hearing

            oscillator.start();
            oscillator.stop(context.currentTime + 0.1); // Short test sound
            return true; // Assume success if no error
        } catch (error) {
            console.error('Audio output test failed:', error);
            return false;
        }
    }

    protected async closeSilently(context: OverridenAudioContext | null): Promise<void> {
        debugLog?.log(`close:`, Log.ref(context));
        if (!context)
            return;

        if (context.state === 'closed')
            return;

        context.destinationFallback?.dispose();
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

    protected suspendContextDebounced = debounce(this.suspendContext, SuspendDebounceTimeMs);
    protected async suspendContext(): Promise<void> {
        infoLog?.log('suspendContext()');
        const context = this._context;
        if (!context)
            return;

        if (context.state === 'closed') {
            await this.closeContext();
            return;
        }

        await context.suspend();
        context.destinationFallback?.pause();
        if (Interactive.isAlwaysInteractive && AudioInitializer.backgroundState === 'BackgroundIdle')
            this.closeContextDebounced();
    }

    protected closeContextDebounced = debounce(() => this.closeContext(), CloseUnusedContextDebounce);
    protected async closeContext(): Promise<void> {
        infoLog?.log('closeContext()');
        const context = this._context;
        this._context = null;
        await this.closeSilently(context);
        if (AudioInitializer.backgroundState !== 'BackgroundIdle') {
            this.whileBackgroundIdle?.resolve(undefined);
            this.whileBackgroundIdle = null;
        }
    }

    // Event handlers

    protected async onDeviceAwake(durationMs: number): Promise<void> {
        debugLog?.log(`onDeviceAwake`, durationMs);
        if (durationMs < CloseUnusedContextDebounce)
            return; // Skip, as device was sleeping for a short period of time

        this.deviceWokeUpAt = Date.now();

        if (this.hasRefsInUse)
            return; // Context is already being used

        this.isInteractiveWasReset = false;
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        await this.closeContext();
    }


    // Private methods

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

    private createSilenceBuffer(context: AudioContext): AudioBuffer {
        return context.createBuffer(1, 1, this.purpose === 'playback' ? AP.SAMPLE_RATE : AR.SAMPLE_RATE);
    }

    private isWakeUp(): boolean {
        return (Date.now() - this.deviceWokeUpAt) <= WakeUpDetectionIntervalMs;
    }
}

class WebAudioContextSource extends AudioContextSourceBase implements AudioContextSource {
    private _isActive = false;
    private _maintain: Promise<void> | null = null;
    private _whenReady = new PromiseSource<OverridenAudioContext>();
    private _whenNotReady = new PromiseSource<void>();

    // Key properties
    public get isMaintained(): boolean { return this._isActive }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        delayAsync(300)
            .then(() => {
                this._maintain = this.maintain();
            });
    }

    public async whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext> {
        const whenReady = this._whenReady;
        if (whenReady.isCompleted()) {
            const context = await whenReady;
            if (!context || context.state === 'closed' || context !== this._context)
                this.markNotReady(); // Reset ready state
        }
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

        const context = this._context;
        if (context && context.state === 'running') {
            debugLog?.log(`initContextInteractively: already running`);
            return; // Already ready
        } else if (context && context.state === 'suspended') {
            try {
                await this.resume(context, true);
            }
            catch(e) {
                warnLog?.log(`initContextInteractively: failed to resume`, e);
                await context.close();
            }
            return;
        }

        if (!this._isActive) {
            if (this._maintain)
                await this._maintain;
            this._maintain = this.maintain();
        }
    }

    public async reset(): Promise<void> {
        this._isActive = false;
        await this.closeContext();
        if (this._maintain)
            await this._maintain;
        this._maintain = this.maintain();
    }

    public async updateBackgroundState(state: BackgroundState): Promise<void> {
        await super.updateBackgroundState(state);

        if (state === 'BackgroundIdle') {
            this._isActive = false;
            return;
        }

        const context = this._context;
        if (!context)
            return;

        await this.interactiveResume(context);
    }

    public async useRef(ref: AudioContextRef): Promise<Disposable> {
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();
        if (!this._isActive) {
            this._maintain = this.maintain();
            return Disposables.empty();
        }
        const context = await this._whenReady;
        if (context && context.state !== 'running') {
            warnLog?.log('useRef: context is not running', context.state);
        }

        await context?.destinationFallback?.play();

        return Disposables.fromAction(() => {
            const hasRefsInUse = this.hasRefsInUse;
            const backgroundState = AudioInitializer.backgroundState;
            infoLog?.log('pauseRef:', hasRefsInUse, backgroundState);
            if (hasRefsInUse)
                return;

            void context?.destinationFallback?.pause();
        });
    }

    // Must be private, but good to keep it near markNotReady
    private markReady(context: AudioContext): void {
        // Invariant it maintains on exit:
        // - _context != null
        // - _whenReady is completed
        // - _whenNotReady is NOT completed.

        if (this._context === context)
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

        this._context = null;
        debugLog?.log(`markNotReady`);

        // _whenReady must be replaced first
        this._whenReady = new PromiseSource<AudioContext>();

        if (!this._whenNotReady.isCompleted())
            this._whenNotReady.resolve(undefined);
    }

    public break() {
        if (!this._context) {
            warnLog?.log(`break: no AudioContext, so nothing to break`);
            return;
        }

        this._context[Debug.brokenKey] = true;
        warnLog?.log(`break: done`);
    }

    // Protected methods

    protected async closeSilently(context: AudioContext | null): Promise<void> {
        this.markNotReady();
        await super.closeSilently(context);
    }

    // Private methods

    private async maintain(): Promise<void> {
        debugLog?.log('maintain: starting');
        this._isActive = true;

        // noinspection InfiniteLoopJS
        let retryCount = 0;
        while (this._isActive) { // Renew loop
            try {
                // debugLog?.log('maintain: loop 1');
                let context = this._context;
                // Try to maintain existing context and create a new one if it's broken or closed
                // @ts-ignore
                if (!context || context.state === 'closed') {
                    context = await this.create();
                    this.markReady(context);
                }

                if (context.state === 'suspended') {
                    if (context.wasInteractive)
                        await this.resume(context, true);
                    else {
                        // Wait for the next user interaction to resume the context
                        const interactiveResume = this.interactiveResume(context);
                        await Promise.race([this._whenNotReady, interactiveResume]);
                    }
                }
                let lastTestAt = Date.now();

                // noinspection InfiniteLoopJS
                while (this._isActive) { // Fix loop
                    // debugLog?.log('maintain: loop 2');
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
                        // debugLog?.log('maintain: loop 3');
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
                    const whileBackgroundIdle = this.whileBackgroundIdle;
                    if (whileBackgroundIdle)
                        await whileBackgroundIdle;
                    else
                        await firstValueFrom(Interactive.interactionEvent$);
                    warnLog?.log(`maintain: resuming maintain cycle`);
                }
            }
            finally {
                if (this._isActive)
                    await this.closeContext();
            }
        }
    }

    private async fix(context: OverridenAudioContext): Promise<void> {
        debugLog?.log(`fix:`, Log.ref(context));

        try {
            if (!this._isActive)
                return;

            if (!await this.trySuspend(context)) {
                // noinspection ExceptionCaughtLocallyJS
                throw new Error(`${logScope}.fix: couldn't suspend AudioContext`);
            }
            if (context.wasInteractive)
                await this.resume(context, true);
            else
                await this.interactiveResume(context);
            await this.test(context);
            debugLog?.log(`fix: success`, );
        }
        catch (e) {
            warnLog?.log(`fix: failed, error:`, e);
            throw e;
        }
    }

    private async trySuspend(context: OverridenAudioContext): Promise<boolean> {
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
}

class MauiAudioContextSource extends AudioContextSourceBase implements AudioContextSource {
    get isMaintained(): boolean { return true; }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
    }

    public async whenReady(cancel?: Promise<symbol>): Promise<OverridenAudioContext> {
        const context = this._context;
        if (context == null || context.state === 'closed') {
            const whileBackgroundIdle = this.whileBackgroundIdle;
            if (whileBackgroundIdle)
                await whileBackgroundIdle;
            this._context = await this.create();
        }
        return this._context!;
    }

    public async whenNotReady(context: AudioContext, cancel?: Promise<symbol>): Promise<void> {
        const whenContextClosed = firstValueFrom(this.contextClosed$);
        await waitAsync(whenContextClosed, cancel);
    }

    public initContextInteractively(): Promise<void> {
        return ResolvedPromise.Void;
    }

    public async reset(): Promise<void> {
        await this.closeContext();
    }

    public async useRef(ref: AudioContextRef): Promise<Disposable> {
        this.suspendContextDebounced.reset();
        this.closeContextDebounced.reset();
        let context = await this.whenReady();
        if (context.state === 'suspended') {
            const whenDelayCompleted = delayAsync(MaxResumeTimeMs);
            await Promise.race([context.resume(), whenDelayCompleted])
                .then(async () => {
                    if (context.state !== 'running') {
                        void context.close();
                        context = await this.whenReady();
                    }
                    await context.destinationFallback?.play();
                });
        }
        else if (context.state === 'running') {
            // Test the context to ensure it's actually working
            context = await this.ensureContextIsRunning(context);
            await context.destinationFallback?.play();
        }
        else
            await context.destinationFallback?.play();

        return Disposables.fromAction(() => {
            const hasRefsInUse = this.hasRefsInUse;
            const backgroundState = AudioInitializer.backgroundState;
            infoLog?.log('pauseRef:', hasRefsInUse, backgroundState);
            if (hasRefsInUse)
                return;

            this.suspendContextDebounced();
        });
    }


    private async ensureContextIsRunning(context: OverridenAudioContext): Promise<OverridenAudioContext> {
        try {
            // Perform a lightweight test to ensure context is working
            await this.test(context, false); // Non-interactive test
            return context;
        } catch (e) {
            warnLog?.log('useRef: context test failed, recreating context', e);
            // Context is broken, close and recreate
            void context.close();
        }
        return this.whenReady();
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

resetMediaSessionMetadata();
