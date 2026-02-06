import { AUDIO_PLAY as AP, AUDIO_REC as AR } from '_constants';
import { debounce, delayAsync, PromiseSource, ResolvedPromise, waitAsync, Cancelled } from 'promises';
import { Interactive } from 'interactive';
import { InteractiveUI } from "../../UI.Blazor/Services/InteractiveUI/interactive-ui";
import { OnDeviceAwake } from 'on-device-awake';
import { firstValueFrom, Observable, Subject } from 'rxjs';
import { Versioning } from 'versioning';
import { BrowserInfo } from '../../UI.Blazor/Services/BrowserInfo/browser-info';
import { AudioContextTrait, AttachedAudioContextTrait, DestinationFallbackTrait, DemandInteractiveUI } from './audio-context-traits';
import { Log } from 'logging';
import { AudioInitializer, AppActivityState } from './audio-initializer';
import { Disposable } from 'disposable';
import { DeviceInfo } from 'device-info';

const { logScope, infoLog, debugLog, warnLog } = Log.get('AudioContextSource');

const MaintainCyclePeriodMs = 3000;
const MaxResumeTimeMs = 2000;
const ShortTestIntervalMs = 150;
const LongTestIntervalMs = 1000;
const SilencePlaybackDuration = 0.280;
const SuspendDebounceTimeMs: number = 2000;
const CloseUnusedContextDebounce: number = 60000; // 60 seconds usually enough on iOS Safari to make audio context broken while backgrounded

const Debug = {
    brokenKey: 'debugging_isBroken',
}

// Types

export type AudioContextPurpose = 'recording' | 'playback';

export type AppAudioContext = AudioContext & {
    wasInteractive?: boolean;
    traits?: Map<string, AttachedAudioContextTrait>;
    _attachingTraits?: Set<string>;
};

// Utility Functions

export function resetMediaSessionMetadata(): void {
    if ('mediaSession' in navigator) {
        navigator.mediaSession.metadata = new MediaMetadata({
            title: `Ready`,
            artist: 'Voxt',
            artwork: [{ src: '/_applogo-dark_voxt.svg' }]
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

let nextRefId = 1;

/** Usage handle for an AudioContext. Creating a ref signals "in use", disposing signals "unused". */
export class AudioContextRef implements Disposable {
    private readonly _id: number;
    private readonly _traits: AudioContextTrait[];
    private readonly _attachedTraits: Map<string, AttachedAudioContextTrait> = new Map();
    private readonly _whenDisposed = new PromiseSource<void>();
    private _context: AudioContext | null = null;
    private _whenReady = new PromiseSource<AudioContext>();
    private _whenFailed = new PromiseSource<void>();
    private _disposed = false;

    /** The current AudioContext, or null if not ready */
    public get context(): AudioContext | null { return this._context; }

    /** True if context is ready and available */
    public get isReady(): boolean { return this._context !== null; }

    /** True if this ref has been disposed */
    public get isDisposed(): boolean { return this._disposed; }

    /** Traits requested for this ref */
    public get traits(): ReadonlyArray<AudioContextTrait> { return this._traits; }

    /** True when this ref carries the DemandInteractiveUI trait */
    public hasDemandInteractiveUITrait(): boolean {
        return this._traits.includes(DemandInteractiveUI.instance);
    }

    constructor(traits: AudioContextTrait[]) {
        this._id = nextRefId++;
        this._traits = traits;
        debugLog?.log(`AudioContextRef#${this._id} created with traits:`, traits.map(t => t.name));
    }

    /** Promise that resolves when the context becomes ready */
    public whenReady(): Promise<AudioContext> {
        return this._whenReady;
    }

    /** Promise that resolves when the context becomes unavailable (closed or failed) */
    public whenFailed(): Promise<void> {
        return this._whenFailed;
    }

    /** Promise that resolves when this ref is disposed */
    public whenDisposed(): Promise<void> {
        return this._whenDisposed;
    }

    /** Gets an attached trait by its trait definition, or null if not attached */
    public getTrait<T extends AttachedAudioContextTrait>(trait: AudioContextTrait): T | null {
        const attached = this._attachedTraits.get(trait.name);
        return (attached as T) ?? null;
    }

    /** Runs an action with the AudioContext when ready. Auto-cancelled if ref is disposed or context fails. */
    public run(action: (context: AudioContext) => Promise<void>): AudioContextAction {
        return new AudioContextAction(this, action);
    }

    /** Disposes this ref, signaling the context is no longer in use */
    public dispose(): void {
        if (this._disposed)
            return;

        debugLog?.log(`AudioContextRef#${this._id} disposing`);
        this._disposed = true;
        this._attachedTraits.clear();
        this._context = null;

        if (!this._whenFailed.isCompleted())
            this._whenFailed.resolve(undefined);

        this._whenDisposed.resolve(undefined);
    }

    // Internal methods called by AudioContextSource

    /** @internal Called when context becomes ready */
    _setReady(context: AppAudioContext): void {
        if (this._disposed)
            return;

        this._context = context;

        // Copy relevant attached traits for this ref's requested traits
        const contextTraits = context.traits;
        if (contextTraits) {
            for (const trait of this._traits) {
                const attached = contextTraits.get(trait.name);
                if (attached) {
                    this._attachedTraits.set(trait.name, attached);
                }
            }
        }

        if (!this._whenReady.isCompleted()) {
            this._whenReady.resolve(context);
        } else {
            // If already resolved (e.g., context recycled), create new promise sources
            this._whenReady = new PromiseSource<AudioContext>();
            this._whenReady.resolve(context);
        }

        // Reset failed promise if it was completed
        if (this._whenFailed.isCompleted()) {
            this._whenFailed = new PromiseSource<void>();
        }
    }

    /** @internal Called when context becomes unavailable */
    _setFailed(): void {
        if (this._disposed || !this._context)
            return;

        this._context = null;
        this._attachedTraits.clear();

        if (!this._whenFailed.isCompleted()) {
            this._whenFailed.resolve(undefined);
        }

        // Reset ready promise for next context
        if (this._whenReady.isCompleted()) {
            this._whenReady = new PromiseSource<AudioContext>();
        }
    }
}

/** A running action on an AudioContext. Disposing cancels it gracefully. */
export class AudioContextAction implements Disposable {
    private readonly _ref: AudioContextRef;
    private readonly _whenDone = new PromiseSource<void>();
    private _isRunning = true;
    private _disposed = false;

    /** True if the action is still running */
    public get isRunning(): boolean { return this._isRunning; }

    /** Promise that resolves when the action completes or is cancelled */
    public get whenDone(): Promise<void> { return this._whenDone; }

    constructor(ref: AudioContextRef, action: (context: AudioContext) => Promise<void>) {
        this._ref = ref;
        void this.execute(action);
    }

    private async execute(action: (context: AudioContext) => Promise<void>): Promise<void> {
        try {
            // Wait for context to be ready or ref to be disposed
            const context = await Promise.race([
                this._ref.whenReady(),
                this._ref.whenDisposed().then(() => null),
            ]);

            if (!context || this._disposed) {
                return;
            }

            // Run the action, racing against failure/disposal
            await Promise.race([
                action(context),
                this._ref.whenFailed(),
                this._ref.whenDisposed(),
            ]);
        } catch (e) {
            if (!this._disposed) {
                warnLog?.log('AudioContextAction failed:', e);
            }
        } finally {
            this._isRunning = false;
            this._whenDone.resolve(undefined);
        }
    }

    /** Disposes this action, stopping it if still running */
    public dispose(): void {
        if (this._disposed)
            return;

        this._disposed = true;
        this._isRunning = false;

        if (!this._whenDone.isCompleted()) {
            this._whenDone.resolve(undefined);
        }
    }
}

// AudioContextSource
// This is a singleton - one instance per AudioContextPurpose, lives for the entire app lifetime.

export class AudioContextSource {
    // Private fields
    private readonly _traits: Map<string, AudioContextTrait> = new Map();
    private readonly _refs: Set<AudioContextRef> = new Set();
    private readonly _contextCreated$: Subject<AudioContext> = new Subject<AudioContext>();
    private readonly _contextClosed$: Subject<AudioContext> = new Subject<AudioContext>();
    private _testRequested: PromiseSource<void> | null = null;
    private _appActivityState: AppActivityState | null = null;
    private _whileBackgroundIdleAppActivityState: PromiseSource<void> | null = null;
    private _context: AppAudioContext | null = null;
    private _refCount = 0;
    private _isMaintained = false;
    private _maintainTask: Promise<void> | null = null;
    private _whenReady = new PromiseSource<AppAudioContext>();
    private _whenNotReady = new PromiseSource<void>();
    private _suspendContextDebounced = debounce(() => this.suspendContext(), SuspendDebounceTimeMs);
    private _closeContextDebounced = debounce(() => this.closeContext(), CloseUnusedContextDebounce);

    // Public properties
    public readonly contextCreated$: Observable<AudioContext> = this._contextCreated$.asObservable();
    public readonly contextClosed$: Observable<AudioContext> = this._contextClosed$.asObservable();
    public breakProbability = 0;
    public get isContextRunning(): boolean {
        return !!this._context && this._context.state === "running";
    }
    public get isMaintained(): boolean {
        return this._isMaintained;
    }
    public get isUsed(): boolean {
        return this._refCount > 0;
    }

    public constructor(public readonly purpose: AudioContextPurpose) {
        // Subscribe to device wake events - no need to store the handler since this is a singleton
        OnDeviceAwake.events.add((durationMs) => this.onDeviceAwake(durationMs));
        if (purpose === "playback") {
            if ("audioSession" in navigator && typeof navigator.audioSession === "object") {
                (navigator.audioSession as any)["type"] = "playback";
                (navigator.audioSession as any)["type"] = "auto"; // Hack for iOS Safari
                (navigator.audioSession as any)["type"] = "playback";
            }
            resetMediaSessionMetadata();
        }
        // The only case this method starts is application start,
        // so it makes sense let other tasks to make some progress first.
        delayAsync(300).then(() => {
            this._maintainTask = this.maintain();
        });
    }

    public hasTrait(trait: AudioContextTrait): boolean {
        return this._traits.has(trait.name);
    }

    public addTrait(trait: AudioContextTrait): void {
        if (this._traits.has(trait.name)) {
            debugLog?.log(`addTrait: trait '${trait.name}' already registered`);
            return;
        }

        debugLog?.log(`addTrait: registering trait '${trait.name}'`);
        this._traits.set(trait.name, trait);

        // If context is already ready, attach the trait immediately
        const context = this._context;
        if (context && context.state !== "closed") {
            void this.attachTrait(trait, context);
        }
    }

    public hasRefWithDemandInteractiveUITrait(): boolean {
        for (const ref of this._refs)
            if (ref.hasDemandInteractiveUITrait())
                return true;
        return false;
    }

    public createRef(...traits: AudioContextTrait[]): AudioContextRef {
        // Ensure all requested traits are registered
        for (const trait of traits) {
            this.addTrait(trait);
        }

        const ref = new AudioContextRef(traits);
        this._refs.add(ref);

        // Track ref count
        this._refCount++;
        debugLog?.log(`createRef: refCount = ${this._refCount}`);

        // Handle first ref - trigger onUsed callbacks
        if (this._refCount === 1) {
            void this.onFirstRefCreated();
        }

        // If context is ready, set the ref as ready
        if (this._context && this._context.state !== "closed") {
            ref._setReady(this._context);
        }

        // Set up disposal handling
        void ref.whenDisposed().then(() => {
            this._refs.delete(ref);
            this._refCount--;
            debugLog?.log(`createRef.dispose: refCount = ${this._refCount}`);

            if (this._refCount === 0) {
                void this.onLastRefDisposed();
            }
        });

        this._suspendContextDebounced.reset();
        this._closeContextDebounced.reset();

        return ref;
    }

    public run(action: (context: AudioContext) => Promise<void>, ...traits: AudioContextTrait[]): AudioContextAction {
        const ref = this.createRef(...traits);
        return new AudioContextAction(ref, async (context) => {
            try {
                await action(context);
            } finally {
                ref.dispose();
            }
        });
    }

    public async whenReady(cancel?: Promise<Cancelled>): Promise<AudioContext> {
        // Ensure the maintain loop is running so that `_whenReady` can eventually resolve.
        if (!this._isMaintained) {
            debugLog?.log(`whenReady: auto-start maintain (was inactive)`);
            this._maintainTask = this.maintain();
        }

        const whenReady = this._whenReady;
        if (whenReady.isCompleted()) {
            const context = await whenReady;
            if (!context || context.state === "closed" || context !== this._context)
                this.markNotReady(); // Reset ready state
            else return context;
        }
        return waitAsync(this._whenReady, cancel);
    }

    public whenNotReady(context: AudioContext, cancel?: Promise<Cancelled>): Promise<void> {
        if (!context || this._context != context) return ResolvedPromise.Void;

        return waitAsync(this._whenNotReady, cancel);
    }

    public async initContextInteractively(): Promise<void> {
        Interactive.isInteractive = true;
        debugLog?.log(`initContextInteractively()`);

        const context = this._context;
        if (context && context.state === "running") {
            debugLog?.log(`initContextInteractively: already running`);
            return; // Already ready
        } else if (context && context.state === "suspended") {
            try {
                await this.resume(context, true);
            } catch (e) {
                warnLog?.log(`initContextInteractively: failed to resume`, e);
                await context.close();
            }
            return;
        }

        if (!this._isMaintained) {
            if (this._maintainTask) await this._maintainTask;
            this._maintainTask = this.maintain();
        }
    }

    public async reset(): Promise<void> {
        this._isMaintained = false;
        await this.closeContext();
        if (this._maintainTask) await this._maintainTask;
        this._maintainTask = this.maintain();
    }

    public async setAppActivityState(state: AppActivityState): Promise<void> {
        debugLog?.log(`setAppActivityState:`, state, this.isUsed);
        if (state === this._appActivityState) return;

        this._appActivityState = state;
        if (state === "BackgroundIdle") {
            this._whileBackgroundIdleAppActivityState ??= new PromiseSource<void>();
            if (!this.isUsed) this._suspendContextDebounced();
            this._isMaintained = false;
            return;
        } else {
            this._suspendContextDebounced.reset();
            this._closeContextDebounced.reset();
            this._whileBackgroundIdleAppActivityState?.resolve(undefined);
            this._whileBackgroundIdleAppActivityState = null;
        }

        const context = this._context;
        if (!context) return;

        await this.interactiveResume(context);
    }

    public async interactiveResume(context: AppAudioContext): Promise<void> {
        debugLog?.log(`interactiveResume:`, Log.ref(context));
        if (context && this.isRunning(context)) {
            debugLog?.log(`interactiveResume: succeeded (AudioContext is already in running state)`);
            Interactive.isInteractive = true;
            return;
        }

        if (!Interactive.isAlwaysInteractive) await BrowserInfo.whenReady; // This is where isAlwaysInteractive flag gets set - it checked further
        if (Interactive.isAlwaysInteractive) {
            debugLog?.log(`interactiveResume: Interactive.isAlwaysInteractive == true`);
            await this.resume(context, false);
            Interactive.isInteractive = true;
        } else {
            // Resume can be called during user interaction only
            if (this.hasRefWithDemandInteractiveUITrait()) {
                debugLog?.log(`interactiveResume: DemandInteractiveUI refs exist, demanding interaction`);
                void InteractiveUI.demand("listening");
            }
        }

        debugLog?.log(`interactiveResume: waiting for interaction`);
        const resumeTask = new PromiseSource<boolean>();
        // Keep user gesture stack without async!!!
        const handler = Interactive.interactionEvents.add((e) => {
            // this resume should be called without async in the same sync stack as user gesture!!!
            debugLog?.log(`interactiveResume: Interactive.interactionEvents triggered`, e);
            const currentContext = this._context;
            let contextToResume = context;
            if (currentContext && currentContext !== context) {
                warnLog?.log("interactiveResume: context has already been changed, will try to use the new one");
                contextToResume = currentContext;
            }
            if (contextToResume.state === "closed") {
                warnLog?.log("interactiveResume: context is closed, will try to create a new one");
                this.create(true).then(
                    () => resumeTask.resolve(true),
                    (reason) => {
                        warnLog?.log(reason, "create(true) failed with an error");
                        resumeTask.reject(reason);
                    },
                );
                return;
            }
            this.resume(contextToResume, true).then(
                () => resumeTask.resolve(true),
                (reason) => {
                    warnLog?.log(reason, "resume() failed with an error");
                    resumeTask.reject(reason);
                },
            );
        });
        try {
            await resumeTask;
            Interactive.isInteractive = true;
            debugLog?.log(`interactiveResume: succeeded on interaction`);
        } finally {
            handler.dispose();
        }
    }

    public break() {
        if (!this._context) {
            warnLog?.log(`break: no AudioContext, so nothing to break`);
            return;
        }

        this._context[Debug.brokenKey] = true;
        warnLog?.log(`break: done`);
    }

    // Private methods

    private async attachTrait(trait: AudioContextTrait, context: AppAudioContext): Promise<void> {
        // Skip if already attached or currently attaching
        if (context.traits?.has(trait.name) || context._attachingTraits?.has(trait.name)) {
            debugLog?.log(`attachTrait: '${trait.name}' already attached or attaching`);
            return;
        }

        // Mark as attaching to prevent double attach during async operation
        context._attachingTraits ??= new Set();
        context._attachingTraits.add(trait.name);

        try {
            debugLog?.log(`attachTrait: attaching '${trait.name}' to context`, Log.ref(context));
            const attached = await trait.attach(context);

            context.traits ??= new Map();
            context.traits.set(trait.name, attached);

            // If already in use, call onUsed
            if (this._refCount > 0 && attached.onUsed) {
                await attached.onUsed();
            }
        } catch (e) {
            warnLog?.log(`attachTrait: failed to attach '${trait.name}':`, e);
        } finally {
            context._attachingTraits?.delete(trait.name);
        }
    }

    private async attachAllTraits(context: AppAudioContext): Promise<void> {
        context.traits = new Map();
        context._attachingTraits = new Set();
        const attachPromises: Promise<void>[] = [];
        for (const trait of this._traits.values()) {
            attachPromises.push(this.attachTrait(trait, context));
        }
        await Promise.all(attachPromises);
    }

    private async detachAllTraits(context: AppAudioContext | null): Promise<void> {
        if (!context?.traits) return;

        const closePromises: Promise<void>[] = [];
        for (const attached of context.traits.values()) {
            if (attached.onClosed) {
                closePromises.push(
                    Promise.resolve(attached.onClosed()).catch((e) =>
                        warnLog?.log("detachAllTraits: onClosed failed:", e),
                    ),
                );
            }
        }
        await Promise.all(closePromises);
        context.traits.clear();
        context._attachingTraits?.clear();
    }

    private async onFirstRefCreated(): Promise<void> {
        debugLog?.log("onFirstRefCreated");
        this._suspendContextDebounced.reset();
        this._closeContextDebounced.reset();

        // Call onUsed on all attached traits
        const traits = this._context?.traits;
        if (!traits) return;

        const usedPromises: Promise<void>[] = [];
        for (const attached of traits.values()) {
            if (attached.onUsed) {
                usedPromises.push(
                    Promise.resolve(attached.onUsed()).catch((e) =>
                        warnLog?.log("onFirstRefCreated: onUsed failed:", e),
                    ),
                );
            }
        }
        await Promise.all(usedPromises);
    }

    private async onLastRefDisposed(): Promise<void> {
        debugLog?.log("onLastRefDisposed");

        // Call onUnused on all attached traits
        const traits = this._context?.traits;
        if (traits) {
            const unusedPromises: Promise<void>[] = [];
            for (const attached of traits.values()) {
                if (attached.onUnused) {
                    unusedPromises.push(
                        Promise.resolve(attached.onUnused()).catch((e) =>
                            warnLog?.log("onLastRefDisposed: onUnused failed:", e),
                        ),
                    );
                }
            }
            await Promise.all(unusedPromises);
        }

        // Check if should suspend
        const backgroundState = AudioInitializer.appActivityState;
        if (backgroundState === "BackgroundIdle") {
            this._suspendContextDebounced();
        }
    }

    private notifyRefsReady(context: AppAudioContext): void {
        for (const ref of this._refs) {
            if (!ref.isDisposed) {
                ref._setReady(context);
            }
        }
    }

    private notifyRefsFailed(): void {
        for (const ref of this._refs) {
            if (!ref.isDisposed) {
                ref._setFailed();
            }
        }
    }

    private async create(shouldResume: boolean = false): Promise<AudioContext> {
        debugLog?.log(`create`);
        this._suspendContextDebounced.reset();
        this._closeContextDebounced.reset();
        // Try to create audio context early w/o waiting for user interaction.
        // It might be in suspended state in this case.
        const context: AppAudioContext = new AudioContext({
            latencyHint: "balanced",
            sampleRate:
                this.purpose === "playback" ? AP.SAMPLE_RATE : DeviceInfo.isFirefox ? undefined : AR.SAMPLE_RATE, // FF doesn't support sample rate for microphone stream, we will use default
        });

        if (shouldResume) await this.resume(context, true);
        try {
            debugLog?.log(`create: loading modules`);
            const whenWorkletsLoaded = this.loadContextWorklets(context);

            if (!Interactive.isAlwaysInteractive && !shouldResume) await this.interactiveResume(context);

            await whenWorkletsLoaded;

            // Attach all registered traits
            await this.attachAllTraits(context);

            this._contextCreated$.next(context);

            return context;
        } catch (e) {
            warnLog?.log("create: failed to create", e);
            await this.closeSilently(context);
            throw e;
        }
    }

    private async resume(context: AppAudioContext, isInteractive: boolean): Promise<void> {
        debugLog?.log(`resume:`, Log.ref(context), isInteractive);

        const resumeTask = context.resume().then(() => true);

        if (this.isRunning(context)) {
            debugLog?.log(`resume: already resumed, AudioContext:`, Log.ref(context));
            context.wasInteractive = true;
            Interactive.isInteractive = true;
            return;
        }

        const timerTask = delayAsync(MaxResumeTimeMs).then(() => false);
        if (!(await Promise.race([resumeTask, timerTask])))
            throw new Error(`${logScope}.resume: AudioContext.resume() has timed out.`);
        if (!this.isRunning(context))
            throw new Error(`${logScope}.resume: completed resume, but AudioContext.state != 'running'.`);

        context.wasInteractive = true;
        Interactive.isInteractive = true;
        debugLog?.log(`resume: resumed, AudioContext:`, Log.ref(context));
    }

    private async loadContextWorklets(context: AudioContext): Promise<void> {
        try {
            debugLog?.log(`loadContextWorklets: loading modules`);
            if (this.purpose === "playback") {
                const feederWorkletPath = Versioning.mapPath("/dist/feederWorklet.js");
                await context.audioWorklet.addModule(feederWorkletPath);
            } else {
                const vadWorkletPath = Versioning.mapPath("/dist/vadWorklet.js");
                const encoderWorkletPath = Versioning.mapPath("/dist/opusEncoderWorklet.js");
                const whenModule1 = context.audioWorklet.addModule(vadWorkletPath);
                const whenModule2 = context.audioWorklet.addModule(encoderWorkletPath);
                await Promise.all([whenModule1, whenModule2]);
            }
        } catch (e) {
            warnLog?.log(`loadContextWorklets: failed to load modules:`, e);
            await this.closeSilently(context);
            throw e;
        }
    }

    private isRunning(context: AppAudioContext): boolean {
        // This method addresses some weird issues in how AudioContext behaves in different browsers:
        // - Chromium 110 AudioContext can be in 'running' even after
        //   calling constructor, and even without user interaction.
        // - Safari doesn't start incrementing 'currentTime' after 'resume' call,
        //   so we have to warm it up w/ silent audio
        if (context.state !== "running") return false;

        const silenceBuffer = (context["silenceBuffer"] as AudioBuffer) ?? this.createSilenceBuffer(context);
        const source = context.createBufferSource();
        source.buffer = silenceBuffer;
        const destination = DestinationFallbackTrait.getDestination(context);
        source.connect(destination);
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        source.onended = () => source.disconnect();
        context["silenceBuffer"] = silenceBuffer;
        source.start(0);
        // Schedule to stop silence playback in the future
        source.stop(context.currentTime + SilencePlaybackDuration);
        // NOTE(AK): Somehow - sporadically - currentTime starts ticking only when you log the context!
        console.log(`AudioContext is:`, Log.ref(context), `, its currentTime:`, context.currentTime);
        const isRunning = context.state === "running";
        if (isRunning) context[Debug.brokenKey] = undefined;
        return isRunning;
    }

    private async test(context: AudioContext, isLongTest = false): Promise<void> {
        if (context.state !== "running") throw new Error(`${logScope}.test: AudioContext isn't running.`);
        if (context[Debug.brokenKey]) throw new Error(`${logScope}.test: AudioContext is broken via .break() call.`);
        if (this.breakProbability > 0 && Math.random() < this.breakProbability)
            throw new Error(
                `${logScope}.test: AudioContext failed due to breakProbability = ${this.breakProbability}.`,
            );

        const lastTime = context.currentTime;
        const testCycleCount = 5;
        const testIntervalMs = isLongTest ? LongTestIntervalMs : ShortTestIntervalMs;
        for (let i = 0; i < testCycleCount; i++) {
            await delayAsync(testIntervalMs);
            if (context.state !== "running") throw new Error(`${logScope}.test: AudioContext isn't running.`);
            if (context.currentTime != lastTime) break;
            // play silent audio and check state
            else if (this.isRunning(context)) {
                debugLog?.log(`test: AudioContext is running, but currentTime is not changing.`);
            }
        }
        if (context.currentTime == lastTime)
            // AudioContext isn't running
            throw new Error(`${logScope}.test: AudioContext is running, but didn't pass currentTime test.`);
    }

    private async closeSilently(context: AppAudioContext | null): Promise<void> {
        debugLog?.log(`close:`, Log.ref(context));
        if (!context) return;
        if (context.state === "closed") return;

        this.markNotReady();

        // Call onClosed on all attached traits
        await this.detachAllTraits(context);

        try {
            await context.close();
        } catch (e) {
            warnLog?.log(`close: failed to close AudioContext:`, e);
        } finally {
            this._contextClosed$.next(context);
        }
    }

    private async suspendContext(): Promise<void> {
        infoLog?.log("suspendContext()");
        const context = this._context;
        if (!context) return;

        if (context.state === "closed") {
            await this.closeContext();
            return;
        }

        await context.suspend();

        // Call onUnused on all traits when suspending
        if (context.traits) {
            const unusedPromises: Promise<void>[] = [];
            for (const attached of context.traits.values()) {
                if (attached.onUnused) {
                    unusedPromises.push(
                        Promise.resolve(attached.onUnused()).catch((e) =>
                            warnLog?.log("suspendContext: onUnused failed:", e),
                        ),
                    );
                }
            }
            await Promise.all(unusedPromises);
        }

        if (Interactive.isAlwaysInteractive && AudioInitializer.appActivityState === "BackgroundIdle")
            this._closeContextDebounced();
    }

    private async closeContext(): Promise<void> {
        warnLog?.log("closeContext()");

        const context = this._context;
        this._context = null;
        this.notifyRefsFailed();
        await this.closeSilently(context);

        if (AudioInitializer.appActivityState !== "BackgroundIdle") {
            this._whileBackgroundIdleAppActivityState?.resolve(undefined);
            this._whileBackgroundIdleAppActivityState = null;
        }
    }

    private markNotReady(): void {
        // Invariant it maintains on exit:
        // - _context == null
        // - _whenReady is NOT completed
        // - _whenNotReady is completed.

        debugLog?.log(`markNotReady`);

        this._context = null;
        this.notifyRefsFailed();
        // _whenReady must be replaced first
        this._whenReady = new PromiseSource<AudioContext>();
        // Complete _whenNotReady
        if (!this._whenNotReady.isCompleted()) this._whenNotReady.resolve(undefined);
    }

    private setContextAndMarkReady(context: AudioContext): void {
        // Invariant it maintains on exit:
        // - _context != null
        // - _whenReady is completed
        // - _whenNotReady is NOT completed.

        if (this._context === context) return; // Already ready

        this._context = context;
        debugLog?.log(`markReady: AudioContext:`, Log.ref(context));

        // _whenNotReady must be replaced first
        if (this._whenNotReady.isCompleted()) this._whenNotReady = new PromiseSource<void>();
        // Complete _whenReady
        this._whenReady.resolve(context);
        this.notifyRefsReady(context);
    }

    private async onDeviceAwake(durationMs: number): Promise<void> {
        debugLog?.log(`onDeviceAwake`, durationMs);
        if (!this._context) return;

        // Request an immediate short test from the maintain loop;
        // if the test fails, the context will be recreated.
        this._testRequested?.resolve(undefined);
    }

    private async maintain(): Promise<void> {
        debugLog?.log("maintain: starting");
        this._isMaintained = true;

        // noinspection InfiniteLoopJS
        while (this._isMaintained) {
            // Renew loop
            try {
                // debugLog?.log('maintain: loop 1');
                let context = this._context;
                // Try to maintain existing context and create a new one if it's broken or closed
                // @ts-ignore
                if (!context || context.state === "closed") {
                    context = await this.create();
                    this.setContextAndMarkReady(context);
                }

                if (context.state === "suspended") {
                    const whileIdle = this._whileBackgroundIdleAppActivityState;
                    if (whileIdle) await whileIdle;
                    if (context.wasInteractive) await this.resume(context, true);
                    else {
                        // Wait for the next user interaction to resume the context
                        const interactiveResume = this.interactiveResume(context);
                        await Promise.race([this._whenNotReady, interactiveResume]);
                    }
                }
                debugLog?.log("maintain: context is running");
                let lastTestAt = Date.now();

                // noinspection InfiniteLoopJS
                while (this._isMaintained) {
                    // Health check loop
                    this._testRequested = new PromiseSource<void>();
                    const testRequested = this._testRequested;
                    const minDelay = lastTestAt + MaintainCyclePeriodMs - Date.now();
                    if (minDelay > 0) {
                        await Promise.race([delayAsync(minDelay), testRequested]);
                    } else {
                        const whenDelayCompleted = delayAsync(MaintainCyclePeriodMs);
                        await Promise.race([this._whenNotReady, whenDelayCompleted, testRequested]);
                    }

                    if (!this._isMaintained) break;

                    const isWakeUpTest = testRequested.isCompleted();
                    lastTestAt = Date.now();
                    try {
                        await this.test(context, !isWakeUpTest);
                    } catch (e) {
                        if (isWakeUpTest && !Interactive.isAlwaysInteractive) {
                            // After wake-up, the browser may have revoked audio permission;
                            // force a new user gesture on next context creation.
                            Interactive.isInteractive = false;
                        }
                        throw e;
                    }
                }
            } catch (e) {
                warnLog?.log(`maintain: error:`, e);
                this.markNotReady();
            }
            await this.closeContext();
        }
    }

    private createSilenceBuffer(context: AudioContext): AudioBuffer {
        return context.createBuffer(1, 1, this.purpose === "playback" ? AP.SAMPLE_RATE : AR.SAMPLE_RATE);
    }
}

// =====================================================
// Init
// =====================================================

export const audioContextSource = BrowserInfo.hostKind === 'MauiApp'
    ? null! as AudioContextSource
    : new AudioContextSource('playback');
globalThis.audioContextSource = audioContextSource;

export const recordingAudioContextSource = BrowserInfo.hostKind === 'MauiApp'
    ? null! as AudioContextSource
    : new AudioContextSource('recording');
globalThis.recordingAudioContextSource = recordingAudioContextSource;

if (BrowserInfo.hostKind !== 'MauiApp') {
    resetMediaSessionMetadata();

    // Register DestinationFallbackTrait for iOS Safari
    if (DestinationFallbackTrait.isRequired) {
        audioContextSource.addTrait(new DestinationFallbackTrait());
    }
}
