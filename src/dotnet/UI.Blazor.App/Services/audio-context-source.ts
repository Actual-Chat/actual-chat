import { BrowserInfo } from '../../UI.Blazor/Services/BrowserInfo/browser-info';
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
import { Log } from 'logging';
import { Versioning } from 'versioning';
import { AudioContextRef, AudioContextRefOptions } from './audio-context-ref';
import { firstValueFrom, Observable, Subject } from 'rxjs';
import { AudioContextDestinationFallback } from './audio-context-destination-fallback';
import * as playerConstants from '../Components/AudioPlayer/constants';
import * as recorderConstants from '../Components/AudioRecorder/constants';

const { logScope, infoLog, debugLog, warnLog } = Log.get('AudioContextSource');

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
const SuspendDebounceTimeMs: number = 2000;

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

    terminate(): Promise<void>;

    resumeAudio(): Promise<void>;

    suspendAudio(): Promise<void>;

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

    protected constructor(public readonly purpose: AudioContextPurpose) {
        this.onDeviceAwakeHandler = OnDeviceAwake.events.add(() => this.onDeviceAwake());
        if (purpose === 'playback' && AudioContextDestinationFallback.isRequired) {
            this.fallbackDestination = new AudioContextDestinationFallback();
        }
        if ('mediaSession' in navigator) {
            navigator.mediaSession.playbackState = 'none';
        }
        if ('audioSession' in navigator) {
            navigator.audioSession['type'] = 'play-and-record'; // 'playback'
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

    public abstract terminate(): Promise<void>;

    public abstract resumeAudio(): Promise<void>;

    public abstract suspendAudio(): Promise<void>;

    public abstract useRef(): void;

    public abstract pauseRef(): void;

    protected async loadContextWorklets(context: AudioContext): Promise<void> {
        try {
            debugLog?.log(`create: loading modules`);
            const feederWorkletPath = Versioning.mapPath('/dist/feederWorklet.js');
            const encoderWorkletPath = Versioning.mapPath('/dist/opusEncoderWorklet.js');
            const vadWorkerPath = Versioning.mapPath('/dist/vadWorklet.js');
            const whenModule1 = context.audioWorklet.addModule(feederWorkletPath);
            const whenModule2 = context.audioWorklet.addModule(encoderWorkletPath);
            const whenModule3 = context.audioWorklet.addModule(vadWorkerPath);
            await Promise.all([whenModule1, whenModule2, whenModule3]);
        }
        catch (e) {
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
    private whenInitializedInteractively: Promise<void> | null = null;
    private _whenReady = new PromiseSource<AudioContext | null>();
    // private _whenClosed = new PromiseSource<AudioContext | null>();
    private _whenNotReady = new PromiseSource<void>();
    private isTerminated = false;

    // Key properties
    public get isActive(): boolean { return this._isActive }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
        void this.maintain();
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

    public async terminate(): Promise<void> {
        this.isTerminated = true;
        this._isActive = false;
        await this.closeSilently(this._context);
    }

    public async resumeAudio(): Promise<void> {
        debugLog?.log(`resumeAudio:`, this._isActive);
        if (!this._isActive)
            void this.maintain();
    }

    public async suspendAudio(): Promise<void> {
        debugLog?.log(`suspendAudio:`, this._isActive);
        this._isActive = false;
        this.markNotReady();
    }

    public pauseRef(): void { }

    public useRef(): void { }

    // Must be private, but good to keep it near markNotReady
    private markReady(context: AudioContext | null) {
        if (!this._isActive)
            return;

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
        this._isActive = true;
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        await delayAsync(300);
        // noinspection InfiniteLoopJS
        let retryCount = 0;
        while (this._isActive) { // Renew loop
            let context: AudioContext = null;
            try {
                let whenInitializedInteractively = this.whenInitializedInteractively as PromiseSource<void>;
                if (!whenInitializedInteractively) {
                    whenInitializedInteractively = new PromiseSource<void>();
                    try {
                        this.whenInitializedInteractively = whenInitializedInteractively;
                        context = await this.create();
                        await this.warmup(context);
                        retryCount = 0;
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
                while (this._isActive) { // Fix loop
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
                        if (this.isTerminated)
                            return;

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
                if (!this.isTerminated)
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
            sampleRate: this.purpose === 'playback' ? playerConstants.SAMPLE_RATE : recorderConstants.SAMPLE_RATE,
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
                await this.interactiveResume(context);
            }

            await Promise.all([whenModule1, whenModule2, whenModule3]);
            if (this.fallbackDestination) {
                context.destinationOverride = this.fallbackDestination.destination;
                await this.fallbackDestination.attach(context);
            }

            return context;
        }
        catch (e) {
            await this.closeSilently(context);
            throw e;
        }
    }

    private async warmup(context: AudioContext): Promise<void> {
        debugLog?.log(`warmup, AudioContext:`, Log.ref(context));
        if (!this._isActive)
            return;

        const warmUpWorkletPath = Versioning.mapPath('/dist/warmUpWorklet.js');
        await context.audioWorklet.addModule(warmUpWorkletPath);
        const nodeOptions: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
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

    private async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (!this._isActive)
            return;

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
        return context.createBuffer(1, 1, this.purpose === 'playback' ? playerConstants.SAMPLE_RATE : recorderConstants.SAMPLE_RATE);
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
    get isActive(): boolean { return true; }

    public constructor(purpose: AudioContextPurpose) {
        super(purpose);
    }

    public async whenReady(cancel?: Promise<symbol>): Promise<AudioContext> {
        if (this._context == null || this._context.state === 'closed') {
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

    public async terminate(): Promise<void> {
        await this.closeSilently(this._context);
        this._context = null;
    }

    public async resumeAudio(): Promise<void> {
        const context = this._context;
        if (context && context.state !== 'closed' && this._refCount > 0) {
            await context.resume();
            await this.fallbackDestination?.play();
        }
    }

    public async suspendAudio(): Promise<void> {
        const context = this._context;
        if (context && context.state !== 'closed')
            await context.suspend();
        this.fallbackDestination?.pause();
    }

    public pauseRef(): void {
        const hasActiveRefs = [...this.refs.values()]
            .reduce((prev, current) =>
                prev || current
                    .map(value => value.state === 'running')
                    .reduce((p, v) => p || v), false);

        if (!hasActiveRefs)
            this.suspendContextDebounced();
    }

    public useRef(): void {
        this.suspendContextDebounced.reset();
        if (!this._context || this._context.state === 'closed')
            void this.whenReady();
        else if (this._context.state === 'suspended')
            void this._context.resume().then(() => {
                infoLog?.log('useRef: resume context');
            });
    }

    private suspendContextDebounced = debounce(this.suspendContext, SuspendDebounceTimeMs);
    private async suspendContext(): Promise<void> {
        infoLog?.log('suspendContext()');
        return this._context.suspend();
    }

    private async create(): Promise<AudioContext> {
        debugLog?.log(`create`);

        const context = new AudioContext({
            latencyHint: 'balanced',
            sampleRate: this.purpose === 'playback' ? playerConstants.SAMPLE_RATE : recorderConstants.SAMPLE_RATE,
        });
        Interactive.isInteractive = true;
        await context.resume();
        await this.fallbackDestination?.attach(context);
        await this.loadContextWorklets(context);

        this._contextCreated$.next(context);
        return context;
    }

    protected async onDeviceAwake(): Promise<void> {
        debugLog?.log(`onDeviceAwake`);
        // Close current AudioContext as it might be corrupted and can produce clicking sound
        await this.closeSilently(this._context);
    }
}


// Init

export const audioContextSource: AudioContextSource = BrowserInfo.appKind === "MauiApp"
    ? new MauiAudioContextSource('playback')
    : new WebAudioContextSource('playback');
globalThis['audioContextSource'] = audioContextSource;

export const recordingAudioContextSource: AudioContextSource = BrowserInfo.appKind === "MauiApp"
    ? new MauiAudioContextSource('recording')
    : new WebAudioContextSource('recording');
globalThis['recordingAudioContextSource'] = recordingAudioContextSource;

