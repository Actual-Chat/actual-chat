import { getLogs } from 'logging';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment, WorkerKind } from 'api';
import type { VideoFormatDto, VideoFrameDto } from 'api';
import { DeviceOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import { RpcStream } from 'actuallab-rpc';
import { OnDeviceAwake } from 'on-device-awake';
import { WebCodecsCompat, type WebCodecsLevelOverride } from 'web-codecs-compat/init';
import { getWebCodecsLevelOverride, setWebCodecsLevelOverride } from 'web-codecs-compat/settings';
import { SvgCache } from '../../Components/Avatar/svg-cache';
import { VirtualListOverlay } from '../../Components/VirtualList/virtual-list-overlay';
import { isEditable } from 'keyboard-visibility';

const { infoLog } = getLogs('DebugUI');

interface BlazorEventDescriptor {
    eventName: string;
    eventHandlerId: number;
}

interface BlazorInternal {
    endInvokeDotNetFromJS: (asyncCallId: number, success: boolean, resultOrError: string) => unknown;
}

interface BlazorGlobal {
    _internal?: BlazorInternal;
}

/** Mirrors the C# `GuideType` enum — picks which platform guide a troubleshooter
 *  modal renders. Omit it to let the modal auto-detect from HostInfo/BrowserInfo. */
type GuideType =
    | 'MobileChromeAndroid'
    | 'MobileEdgeAndroid'
    | 'MobileSafariIos'
    | 'WebChrome'
    | 'WebEdge'
    | 'WebSafari'
    | 'IosApp'
    | 'AndroidApp'
    | 'Unknown';

type VideoTraceKillKind = 'recording' | 'playback';
type VideoTraceKillPeriodInput = number | string;

interface VideoTraceKillGlobal {
    __setVideoTraceKill?: (
        kind: VideoTraceKillKind,
        avgPeriod: VideoTraceKillPeriodInput,
        stage: number | string,
    ) => boolean;
}

export class DebugUI {
    private static _backendRef: DotNet.DotNetObject = null!;
    private static _eventSnifferInstalled = false;
    private static _audioRecorderOffsetHandler: ((offsetMs: number) => void) | null = null;
    private static _rotateTimer: number | null = null;
    // Keyboard heights as a fraction of the full viewport, matching a real S23 Ultra (~45% text, ~40%
    // numeric) so content compression tests realistically; pass px to showKeyboard for an exact height.
    private static readonly _textKeyboardRatio = 0.45;
    private static readonly _numberKeyboardRatio = 0.40;
    // Slide duration for the simulated keyboard - a real one animates in ~0.25s, so the modal reflows
    // progressively rather than snapping. Override per-call via showKeyboard's durationMs.
    private static readonly _keyboardAnimMs = 250;
    private static _kbTweenRaf: number | null = null;
    private static _kbOpenness = 0;
    private static _kbHeight = 0;
    private static _kbEl: HTMLElement | null = null;
    private static _keyboardAutoHandlers: { focusin: (e: FocusEvent) => void; focusout: (e: FocusEvent) => void } | null = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        infoLog?.log(`init`);
        this._backendRef = backendRef1;
        globalThis.debugUI = this;
    }

    // Local-dev-only: stops the running server. Mirrors the HTTP /health/stop
    // endpoint and the 's' keyboard shortcut from CommandLineHandler.
    // Enforcement lives on the server (DebugUI.StopServer); no client-side check.
    public static stopServer(): void {
        infoLog?.log(`stopServer: stopping the server...`);
        void this._backendRef.invokeMethodAsync('StopServer');
    }

    public static async chatMaintenance(chatId: string, isEnabled: boolean): Promise<void> {
        await this._backendRef.invokeMethodAsync('ChatMaintenance', chatId, isEnabled);
    }

    public static async getThreadPoolSettings(): Promise<string> {
        const settings = await this._backendRef.invokeMethodAsync('GetThreadPoolSettings');
        console.log(settings);
        return settings as string;
    }

    public static async changeThreadPoolSettings(
        min: number, minIO: number, max: number, maxIO: number): Promise<string> {
        await this._backendRef.invokeMethodAsync('ChangeThreadPoolSettings', min, minIO, max, maxIO);
        return await this.getThreadPoolSettings();
    }

    public static disconnectBlazorRpc(): void {
        void this._backendRef.invokeMethodAsync('DisconnectRpc');
    }

    public static navigateTo(url: string): void {
        void this._backendRef.invokeMethodAsync('NavigateTo', url);
    }

    /** Debug-only: force-disconnect the RPC peer for one target — see
     *  {@link Api.disconnect}. Pass `'All'` (or omit) to disconnect every
     *  {@link WorkerKind}. */
    public static disconnectJSRpc(workerKind: WorkerKind | 'All' = 'All'): void {
        infoLog?.log(`disconnectJSRpc:`, workerKind);
        if (workerKind === 'All') {
            for (const kind of Object.values(WorkerKind))
                Api.disconnect(kind);
        } else {
            Api.disconnect(workerKind);
        }
    }

    public static fakeSleep(duration = 5): void {
        OnDeviceAwake.fakeSleep(duration * 1000);
    }

    /** Applies on reload: the polyfill cannot be swapped under a running pipeline. */
    public static overrideWebCodecsPolyfillLevel(level?: WebCodecsLevelOverride | null): string {
        setWebCodecsLevelOverride(level ?? 'auto');
        const stored = getWebCodecsLevelOverride();
        const message = `WebCodecs level override: '${stored}' `
            + `(active until reload: '${WebCodecsCompat.level}')`;
        console.log(message);

        return message;
    }

    public static getActiveWebCodecsLevel(): string {
        return WebCodecsCompat.level;
    }

    /** Emits `count` log entries to the LogUI ring buffer for testing the log
     *  viewer. Each entry is `lineCount` lines of placeholder text. */
    public static testLog(count = 1, lineCount = 1): Promise<void> {
        infoLog?.log(`testLog: count=${count}, lineCount=${lineCount}`);
        return this._backendRef.invokeMethodAsync('TestLog', count, lineCount) as unknown as Promise<void>;
    }

    public static resetOnboarding(enable: boolean): void {
        void this._backendRef.invokeMethodAsync('ResetOnboarding', enable);
    }

    public static resetBubbles(enable: boolean): void {
        void this._backendRef.invokeMethodAsync('ResetBubbles', enable);
    }

    public static enableAudioSync(enable = true): void {
        void this._backendRef.invokeMethodAsync('EnableAudioSync', enable);
    }

    // Forces what every recording control reports, so the failure paths can be seen without
    // breaking a microphone. Takes '<Kind>' or '<Kind>:<code>'; omit the argument to clear.
    // Kinds: Off, Starting, Recording, Reconnecting, Disconnected, NoMicrophonePermission,
    // NoMicrophone, MicrophoneBusy, StartFailed.
    public static forceRecordingStatus(status = ''): void {
        void this._backendRef.invokeMethodAsync('ForceRecordingStatus', status);
    }

    public static async getUserId(): Promise<string> {
        const id = await this._backendRef.invokeMethodAsync<string>('GetUserId');
        console.log(`getUserId:`, id);
        return id;
    }

    public static signIn(
        phoneOrEmail: string,
        options?: { register?: boolean; skipOnboarding?: boolean; skipBubbles?: boolean },
    ): Promise<void> {
        const o = options ?? {};
        return this._backendRef.invokeMethodAsync(
            'SignIn',
            phoneOrEmail,
            o.register ?? true,
            o.skipOnboarding ?? true,
            o.skipBubbles ?? true,
        ) as unknown as Promise<void>;
    }

    public static signOut(): Promise<void> {
        return this._backendRef.invokeMethodAsync('SignOut') as unknown as Promise<void>;
    }

    /** Returns the current effective render mode. Reads the `app-server` /
     *  `app-wasm` class that BrowserInfo writes onto `<body>` based on the
     *  resolved HostKind — that's the same source the rest of the app uses,
     *  so it stays correct after Auto's prerender → WASM upgrade. Returns
     *  `'s'` if the body class hasn't been written yet (i.e. still in the
     *  initial server-prerender phase). */
    public static getCurrentRenderMode(): 's' | 'w' {
        return document.body.classList.contains('app-wasm') ? 'w' : 's';
    }

    public static setRenderMode(mode: 'a' | 's' | 'w'): Promise<void> {
        return this._backendRef.invokeMethodAsync('SetRenderMode', mode) as unknown as Promise<void>;
    }

    public static showMicTroubleshooter(guideType?: GuideType): void {
        void this._backendRef.invokeMethodAsync('ShowMicTroubleshooter', guideType ?? null);
    }

    public static showPhotoTroubleshooter(): void {
        void this._backendRef.invokeMethodAsync('ShowPhotoTroubleshooter');
    }

    public static showLocationTroubleshooter(guideType?: GuideType): void {
        void this._backendRef.invokeMethodAsync('ShowLocationTroubleshooter', guideType ?? null);
    }

    public static showIncomingShareModal(): void {
        void this._backendRef.invokeMethodAsync('ShowIncomingShareModal');
    }

    /** Drives the recording quality controller through a synthetic
     *  -1 / 0 / +1 signal sweep over `period` seconds. ~10% of time at
     *  neutral, 45% at "drop", 45% at "raise". Verify via server logs:
     *  ChangeRecordingQuality calls should walk min↔max layer count. */
    public static testVideoRecordingQualityChange(period = 30): void {
        void this._backendRef.invokeMethodAsync('TestVideoRecordingQualityChange', period);
    }

    /** Drives the playback CapacityEstimator through the same -1 / 0 / +1
     *  sweep. Pushes ChangePlaybackQuality info-only payloads (no actual
     *  receive-quality changes); verify via server logs. */
    public static testVideoPlaybackQualityChange(period = 30): void {
        void this._backendRef.invokeMethodAsync('TestVideoPlaybackQualityChange', period);
    }

    // Override the device-orientation pipeline. `degrees` is CW from natural
    // portrait — accepts 0/90/180/270 (or raw 0..3 quarter indices). The
    // override flows through DeviceOrientation → SharedSettings; workers
    // pick it up automatically.
    public static setDeviceOrientation(degrees = 0): RotationQuarter {
        const value = Math.abs(degrees) >= 4 ? degrees / 90 : degrees;
        const quarter = normalizeRotationQuarter(value);
        DeviceOrientation.set(quarter);
        infoLog?.log(`setDeviceOrientation: quarter=${quarter}`);
        return quarter;
    }

    /** Auto-rotates the device orientation at `rpm` revolutions per minute.
     *  Positive RPM cycles CW (0→1→2→3→0); negative cycles CCW. Pass 0 or
     *  null to stop. */
    public static rotateDevice(rpm: number | null = 0): void {
        if (this._rotateTimer !== null) {
            clearInterval(this._rotateTimer);
            this._rotateTimer = null;
        }
        if (rpm === null || !Number.isFinite(rpm) || rpm === 0) {
            infoLog?.log(`rotateDevice: stopped`);
            return;
        }

        const step: 1 | -1 = rpm > 0 ? 1 : -1;
        // 1 revolution = 4 quarter-turns ⇒ each quarter takes 60_000/(4*|rpm|) ms.
        const intervalMs = Math.max(50, Math.round(60_000 / (4 * Math.abs(rpm))));
        let q: RotationQuarter = DeviceOrientation.quarter;
        this._rotateTimer = setInterval(() => {
            q = normalizeRotationQuarter(q + step);
            DeviceOrientation.set(q);
        }, intervalMs) as unknown as number;
        infoLog?.log(`rotateDevice: rpm=${rpm} intervalMs=${intervalMs} step=${step}`);
    }

    public static killVideoRecording(
        avgPeriod: VideoTraceKillPeriodInput = 10, killStage: number | string = 3): boolean {
        return this.setVideoTraceKill('recording', avgPeriod, killStage);
    }

    public static killVideoPlayback(
        avgPeriod: VideoTraceKillPeriodInput = 10, killStage: number | string = 63): boolean {
        return this.setVideoTraceKill('playback', avgPeriod, killStage);
    }

    /** OpusMediaRecorder registers a handler at init time. Stored here so
     *  DebugUI doesn't need to import the higher-level recorder module. */
    public static registerAudioRecorderOffsetHandler(handler: (offsetMs: number) => void): void {
        this._audioRecorderOffsetHandler = handler;
    }

    /** Debug-only: forces the audio recorder to add `offsetMs` ms to the
     *  source timestamp it sends with every new PushStream. Lets us simulate
     *  audio drift for the catch-up policy. Pass 0 to clear. */
    public static setAudioRecorderOffset(offsetMs: number): void {
        infoLog?.log(`setAudioRecorderOffset: ${offsetMs}ms`);
        if (this._audioRecorderOffsetHandler === null) {
            console.warn('setAudioRecorderOffset: handler not registered yet');
            return;
        }

        this._audioRecorderOffsetHandler(offsetMs);
    }

    public static clearSvgCache(): void {
        SvgCache.clear();
        infoLog?.log('clearSvgCache: done');
    }

    public static showSafeAreas(show: boolean | null | undefined): void {
        const cl = document.body.classList;
        cl.remove('show-safe-areas', 'hide-safe-areas');
        if (show === true)
            cl.add('show-safe-areas');
        else if (show === false)
            cl.add('hide-safe-areas');
        infoLog?.log(`showSafeAreas: ${show ?? 'default'}`);
    }

    // Flips between forced 34px insets and the real env() values; bound to the dev-only Ctrl+Shift+L, S chord.
    public static toggleSafeAreas(): void {
        this.showSafeAreas(document.body.classList.contains('show-safe-areas') ? null : true);
    }

    /** Simulates the on-screen keyboard on desktop (e.g. Chrome device toolbar): slides up a visible bottom
     *  overlay (with a collapse button) AND shrinks the vars modals read (--vh, --modal-vh) + sets
     *  body.keyboard-open, so modals collapse just as on a device. The overlay is what makes it visible even
     *  where nothing sizes to --modal-vh - e.g. the left-panel search, which isn't a modal. `heightPx` is the
     *  keyboard height in CSS px; omit for the text default. `debug-keyboard-forced` makes the real
     *  visualViewport listeners stand down so a stray scroll can't clobber the override. Call hideKeyboard. */
    public static showKeyboard(heightPx?: number, durationMs = DebugUI._keyboardAnimMs): void {
        const full = window.innerHeight;
        const height = Math.max(0, Math.min(heightPx ?? Math.round(full * this._textKeyboardRatio), full));
        document.body.classList.add('keyboard-open', 'debug-keyboard-forced');
        this.animateKeyboard(height, 1, durationMs);
        infoLog?.log(`showKeyboard: height=${height}px, ${durationMs}ms`);
    }

    public static showTextKeyboard(): void {
        this.showKeyboard(Math.round(window.innerHeight * this._textKeyboardRatio));
    }

    public static showNumberKeyboard(): void {
        this.showKeyboard(Math.round(window.innerHeight * this._numberKeyboardRatio));
    }

    /** Reverts showKeyboard: slides the overlay back down, hands --vh/--modal-vh back to the live viewport,
     *  and drops the flags - all once the slide lands. Also the collapse button's action. */
    public static hideKeyboard(durationMs = DebugUI._keyboardAnimMs): void {
        const height = this._kbHeight || Math.round(window.innerHeight * this._textKeyboardRatio);
        // Keep debug-keyboard-forced set through the slide so init.ts's listener can't snap --vh mid-motion;
        // drop it (and keyboard-open) and hide the overlay only once it's fully retracted.
        this.animateKeyboard(height, 0, durationMs, () => {
            document.body.classList.remove('keyboard-open', 'debug-keyboard-forced');
            if (this._kbEl)
                this._kbEl.style.display = 'none';
        });
        infoLog?.log(`hideKeyboard: ${durationMs}ms`);
    }

    /** Auto-keyboard mode for desktop mobile-emulation: while on, focusing any input/textarea/
     *  contenteditable pops the simulated keyboard (numeric fields get the shorter numeric height) and
     *  blurring to a non-editable hides it - so the page behaves like a real device without manual
     *  show/hide calls. Reuses keyboard-visibility's isEditable; editable→editable focus keeps it up. */
    public static enableMobileKeyboard(): void {
        if (this._keyboardAutoHandlers)
            return;
        const focusin = (e: FocusEvent): void => {
            if (!isEditable(e.target))
                return;
            const ratio = DebugUI.isNumericField(e.target)
                ? DebugUI._numberKeyboardRatio
                : DebugUI._textKeyboardRatio;
            DebugUI.showKeyboard(Math.round(window.innerHeight * ratio));
        };
        const focusout = (e: FocusEvent): void => {
            // Editable→editable keeps the keyboard up; only leaving to a non-editable tears it down.
            if (isEditable(e.relatedTarget))
                return;
            DebugUI.hideKeyboard();
        };
        document.addEventListener('focusin', focusin);
        document.addEventListener('focusout', focusout);
        this._keyboardAutoHandlers = { focusin, focusout };
        infoLog?.log(`enableMobileKeyboard: on`);
    }

    public static disableMobileKeyboard(): void {
        const handlers = this._keyboardAutoHandlers;
        if (handlers) {
            document.removeEventListener('focusin', handlers.focusin);
            document.removeEventListener('focusout', handlers.focusout);
            this._keyboardAutoHandlers = null;
        }
        this.hideKeyboard();
        infoLog?.log(`disableMobileKeyboard: off`);
    }

    /** On-demand: makes every live InfiniteList compare its geometry model against the DOM after each
     *  render and layout, warning when an item has drifted from where the model says it is. */
    public static virtualListDebug(enable = true): void {
        const vl = (globalThis as Record<string, unknown>).InfiniteList as
            { setDebugEnabled?: (e: boolean) => unknown } | undefined;
        if (!vl?.setDebugEnabled) {
            console.warn('virtualListDebug: InfiniteList is not loaded yet');
            return;
        }

        vl.setDebugEnabled(enable);
        infoLog?.log(`virtualListDebug: ${enable ? 'enabled' : 'disabled'}`);
    }

    /** Toggles a one-line metrics overlay on every virtual list — infinite (chat, content tabs) and
     *  finite (sidebar) alike: render direction, sticky edge, spacers, loaded ends, in-flight request.
     *  Persisted in UserAppSettings, so it survives reloads and follows the account. */
    public static showVirtualListOverlay(enable = true): void {
        VirtualListOverlay.setEnabled(enable);
        void this._backendRef.invokeMethodAsync('SetVirtualListOverlay', enable);
        infoLog?.log(`showVirtualListOverlay: ${enable ? 'enabled' : 'disabled'}`);
    }

    // Applies the persisted setting on startup — no write-back, unlike showVirtualListOverlay.
    public static applyVirtualListOverlay(enable: boolean): void {
        VirtualListOverlay.setEnabled(enable);
    }

    public static startFusionMonitor(): void {
        void this._backendRef.invokeMethodAsync('StartFusionMonitor');
    }

    public static startTaskMonitor(): void {
        void this._backendRef.invokeMethodAsync('StartTaskMonitor');
    }

    public static startDOMEventSniffer(): void {
        if (this._eventSnifferInstalled) {
            infoLog?.log('startDOMEventSniffer: already installed');
            return;
        }

        this._eventSnifferInstalled = true;

        const recentEvents: Record<string, unknown>[] = [];
        const MAX = 50;

        const push = (entry: Record<string, unknown>) => {
            recentEvents.push(entry);
            if (recentEvents.length > MAX)
                recentEvents.shift();
        };

        const snapshot = (): Record<string, unknown>[] =>
            JSON.parse(JSON.stringify(recentEvents)) as Record<string, unknown>[];

        // 1. Blazor-level interception via pre-start hook
        let hasBlazorHook = false;
        const hook = (globalThis as Record<string, unknown>).__blazorEventSnifferHook as
            | ((cb: (desc: BlazorEventDescriptor) => void) => void)
            | undefined;
        if (hook) {
            hook((eventDescriptor) => {
                const eventName = eventDescriptor.eventName;
                const handlerId = eventDescriptor.eventHandlerId;
                const entry = {
                    time: new Date().toISOString().slice(11, 23),
                    src: 'blazor',
                    eventName,
                    handlerId,
                };
                push(entry);
                console.debug('Blazor dispatch:', entry.eventName, 'handlerId=' + String(entry.handlerId));
            });
            hasBlazorHook = true;
        }

        // 2. DOM-level capturing for context
        const TRACK = ['click', 'mouseenter', 'mouseleave', 'pointerdown', 'pointerup', 'focusin', 'focusout'];
        TRACK.forEach((type) => {
            document.addEventListener(
                type,
                (e) => {
                    const target = e.target as Element;
                    push({
                        time: new Date().toISOString().slice(11, 23),
                        src: 'dom',
                        type: e.type,
                        tag: target.tagName,
                        cls: (typeof target.className === 'string' ? target.className : '').substring(0, 60),
                        key: target.closest('[data-key]')?.getAttribute('data-key'),
                    });
                },
                true,
            );
        });

        // 3. Catch NullRef from Blazor's endInvokeDotNetFromJS
        const bi = ((globalThis as Record<string, unknown>).Blazor as BlazorGlobal | undefined)?._internal;
        if (bi?.endInvokeDotNetFromJS) {
            const orig = bi.endInvokeDotNetFromJS;
            bi.endInvokeDotNetFromJS = function (asyncCallId: number, success: boolean, resultOrError: string) {
                if (
                    !success &&
                    typeof resultOrError === 'string' &&
                    (resultOrError.includes('NullReferenceException') ||
                        resultOrError.includes('no event handler') ||
                        resultOrError.includes('DispatchEventAsync'))
                ) {
                    console.error(
                        '%c Blazor event dispatch failed! ',
                        'background:red;color:white;font-weight:bold;padding:2px 6px',
                        '\nCallId:',
                        asyncCallId,
                        '\nError:',
                        resultOrError.substring(0, 200),
                        '\nRecent events:',
                        snapshot(),
                    );
                }
                return orig.call(this, asyncCallId, success, resultOrError);
            };
        }

        // 4. Fallback: unhandled promise rejections
        window.addEventListener('unhandledrejection', (e) => {
            const reason = e.reason as { message?: string; toString?: () => string } | undefined;
            const msg = reason?.message ?? reason?.toString?.() ?? '';
            if (msg.includes('NullReferenceException')) {
                console.error(
                    '%c NullRef caught! ',
                    'background:red;color:white;font-weight:bold;padding:2px 6px',
                    '\nRecent events:',
                    snapshot(),
                );
            }
        });

        infoLog?.log(`startDOMEventSniffer: installed` + (hasBlazorHook ? '' : ' (no Blazor hook)'));
    }

    /**
     * Diagnostic: call `streamingApi.liveVideoStreams.PushStream` directly
     * from the main thread with a synthetic single-keyframe payload. If
     * the server logs `PushStream ENTRY` for `chatId='debug-test'`, the
     * wire shape + auth are fine and the recorder worker has a separate
     * issue. If it doesn't, the problem is in the streamingApi proxy
     * or the wire DTO.
     */
    public static async testPushStream(chatId = 'debug-test'): Promise<string> {
        const peer = Api.peer;
        const format: VideoFormatDto = {
            Codec: 'avc1.42E01F',
            CodecSettings: '',
            Size: { Width: 1280, Height: 720 },
            SourceSize: { Width: 1280, Height: 720 },
        };
        const fakeData = new Uint8Array(64);
        fakeData[0] = 0x42;
        const dto: VideoFrameDto = {
            Data: fakeData,
            Offset: toMoment(0),
            Duration: toMoment(0),
            KeyFrameIndex: 0,
            Index: 0,
            Width: 1280,
            Height: 720,
        };
        const stream = new RpcStream<VideoFrameDto>(
            (async function* () { await Promise.resolve(); yield dto; })(),
            MediaRpcStreamOptions.videoRecording<VideoFrameDto>(
                item => item.KeyFrameIndex !== undefined && item.KeyFrameIndex === item.Index),
        );
        infoLog?.log(`testPushStream: calling PushStream chatId=${chatId} ...`);
        try {
            // 0 = Camera sourceKind. session='~' resolves to Session.Default.
            await streamingApi.liveVideoStreams.PushStream('~', chatId, 0, format, 0, stream.toRef(peer));
            return 'ok';
        } catch (e: unknown) {
            const msg = e instanceof Error ? e.message : String(e);
            infoLog?.log(`testPushStream: rejected: ${msg}`);
            return `error: ${msg}`;
        } finally {
            try {
                stream.disconnect();
            } catch {
                // Intended: the stream may already be disconnected
            }
        }
    }

    // Private methods

    // Drives both the modal viewport vars (--vh/--modal-vh, so modals reflow) and the visible on-screen
    // keyboard overlay from one 0..1 "openness" value, easeOutCubic over durationMs - fast-then-settling
    // like a real keyboard. A new run cancels the previous; onDone fires when it lands.
    private static animateKeyboard(kbHeight: number, target: number, durationMs: number, onDone?: () => void): void {
        if (this._kbTweenRaf !== null) {
            cancelAnimationFrame(this._kbTweenRaf);
            this._kbTweenRaf = null;
        }
        const full = window.innerHeight;
        this._kbHeight = kbHeight;
        const el = this.ensureKeyboardEl();
        el.style.height = `${kbHeight}px`;
        el.style.display = '';
        const from = this._kbOpenness;
        const apply = (o: number): void => {
            this._kbOpenness = o;
            this.setViewport(full - kbHeight * o);
            el.style.transform = `translateY(${(1 - o) * 100}%)`;
        };
        if (durationMs <= 0 || Math.abs(from - target) < 0.001) {
            apply(target);
            onDone?.();
            return;
        }
        const startedAt = performance.now();
        const step = (now: number): void => {
            const t = Math.min(1, (now - startedAt) / durationMs);
            const eased = 1 - (1 - t) ** 3;
            apply(from + (target - from) * eased);
            if (t < 1) {
                this._kbTweenRaf = requestAnimationFrame(step);
            } else {
                this._kbTweenRaf = null;
                onDone?.();
            }
        };
        this._kbTweenRaf = requestAnimationFrame(step);
    }

    private static setViewport(visiblePx: number): void {
        const value = `${visiblePx * 0.01}px`;
        const style = document.documentElement.style;
        style.setProperty('--vh', value);
        style.setProperty('--modal-vh', value);
    }

    // Builds (once) the fake keyboard overlay: a bottom-pinned panel with a collapse button in its
    // bottom-right corner, mirroring Android's hide-keyboard affordance. Sits above modals (a real keyboard
    // is above everything), so the keyboard is visible even where nothing sizes to --modal-vh.
    private static ensureKeyboardEl(): HTMLElement {
        if (this._kbEl)
            return this._kbEl;
        const el = document.createElement('div');
        el.setAttribute('aria-hidden', 'true');
        Object.assign(el.style, {
            position: 'fixed', left: '0', right: '0', bottom: '0',
            zIndex: '2147483000',
            display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: '8px',
            boxSizing: 'border-box', padding: '8px 12px',
            background: '#26262b', borderTop: '1px solid rgba(255,255,255,0.15)',
            color: 'rgba(255,255,255,0.5)', font: '12px/1.2 system-ui, sans-serif',
            transform: 'translateY(100%)', pointerEvents: 'auto', userSelect: 'none',
        } as Partial<CSSStyleDeclaration>);
        const label = document.createElement('span');
        label.textContent = 'Simulated keyboard';
        label.style.alignSelf = 'center';
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.title = 'Collapse keyboard';
        btn.textContent = '⌄';
        Object.assign(btn.style, {
            width: '40px', height: '40px',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            border: 'none', borderRadius: '8px',
            background: 'rgba(255,255,255,0.1)', color: 'rgba(255,255,255,0.85)',
            fontSize: '22px', lineHeight: '1', cursor: 'pointer',
        } as Partial<CSSStyleDeclaration>);
        btn.addEventListener('click', () => DebugUI.hideKeyboard());
        el.append(label, btn);
        document.body.appendChild(el);
        this._kbEl = el;
        return el;
    }

    // Picks the numeric keyboard for fields that would raise it on a device - <input type=number|tel>
    // or any editable with a numeric inputmode.
    private static isNumericField(node: EventTarget | null): boolean {
        const el = node as HTMLElement | null;
        if (!el?.getAttribute)
            return false;
        const type = (el.getAttribute('type') ?? '').toLowerCase();
        const mode = (el.getAttribute('inputmode') ?? '').toLowerCase();
        return type === 'number' || type === 'tel'
            || mode === 'numeric' || mode === 'tel' || mode === 'decimal';
    }

    private static setVideoTraceKill(
        kind: VideoTraceKillKind,
        avgPeriod: VideoTraceKillPeriodInput,
        killStage: number | string,
    ): boolean {
        const hook = (globalThis as VideoTraceKillGlobal).__setVideoTraceKill;
        if (hook === undefined) {
            console.warn(`killVideo${kind === 'recording' ? 'Recording' : 'Playback'}: video trace hook is not loaded`);
            return false;
        }

        return hook(kind, avgPeriod, killStage);
    }
}
