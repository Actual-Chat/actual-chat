import { getLogs } from 'logging';
import { Api, MediaRpcStreamOptions, streamingApi, toMoment, WorkerKind, type VideoFormatDto, type VideoFrameDto } from 'api';
import { DeviceOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import { RpcStream } from 'actuallab-rpc';
import { OnDeviceAwake } from 'on-device-awake';
import { SvgCache } from '../../Components/Avatar/svg-cache';

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
    private static backendRef: DotNet.DotNetObject = null!;
    private static _eventSnifferInstalled = false;
    private static _audioRecorderOffsetHandler: ((offsetMs: number) => void) | null = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        infoLog?.log(`init`);
        this.backendRef = backendRef1;
        globalThis.debugUI = this;
    }

    // Local-dev-only: stops the running server. Mirrors the HTTP /health/stop
    // endpoint and the 's' keyboard shortcut from CommandLineHandler.
    // Enforcement lives on the server (DebugUI.StopServer); no client-side check.
    public static stopServer(): void {
        infoLog?.log(`stopServer: stopping the server...`);
        void this.backendRef.invokeMethodAsync('StopServer');
    };

    public static async getThreadPoolSettings(): Promise<string> {
        const settings = await this.backendRef.invokeMethodAsync('GetThreadPoolSettings');
        console.log(settings);
        return settings as string;
    }

    public static async changeThreadPoolSettings(min: number, minIO: number, max: number, maxIO: number): Promise<string> {
        await this.backendRef.invokeMethodAsync('ChangeThreadPoolSettings', min, minIO, max, maxIO);
        return await this.getThreadPoolSettings();
    };

    public static disconnectBlazorRpc(): void {
        void this.backendRef.invokeMethodAsync('DisconnectRpc');
    };

    public static navigateTo(url: string): void {
        void this.backendRef.invokeMethodAsync('NavigateTo', url);
    };

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
    };

    public static fakeSleep(duration = 5): void {
        OnDeviceAwake.fakeSleep(duration * 1000);
    }

    /** Emits `count` log entries to the LogUI ring buffer for testing the log
     *  viewer. Each entry is `lineCount` lines of placeholder text. */
    public static testLog(count = 1, lineCount = 1): Promise<void> {
        infoLog?.log(`testLog: count=${count}, lineCount=${lineCount}`);
        return this.backendRef.invokeMethodAsync('TestLog', count, lineCount) as unknown as Promise<void>;
    }

    public static resetOnboarding(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetOnboarding', enable);
    };

    public static resetBubbles(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetBubbles', enable);
    };

    public static enableAudioSync(enable = true): void {
        void this.backendRef.invokeMethodAsync('EnableAudioSync', enable);
    };

    public static async getUserId(): Promise<string> {
        const id = await this.backendRef.invokeMethodAsync<string>('GetUserId');
        console.log(`getUserId:`, id);
        return id;
    };

    public static signIn(
        phoneOrEmail: string,
        options?: { register?: boolean; skipOnboarding?: boolean; skipBubbles?: boolean },
    ): Promise<void> {
        const o = options ?? {};
        return this.backendRef.invokeMethodAsync(
            'SignIn',
            phoneOrEmail,
            o.register ?? true,
            o.skipOnboarding ?? true,
            o.skipBubbles ?? true,
        ) as unknown as Promise<void>;
    };

    public static signOut(): Promise<void> {
        return this.backendRef.invokeMethodAsync('SignOut') as unknown as Promise<void>;
    };

    /** Returns the current effective render mode. Reads the `app-server` /
     *  `app-wasm` class that BrowserInfo writes onto `<body>` based on the
     *  resolved HostKind — that's the same source the rest of the app uses,
     *  so it stays correct after Auto's prerender → WASM upgrade. Returns
     *  `'s'` if the body class hasn't been written yet (i.e. still in the
     *  initial server-prerender phase). */
    public static getCurrentRenderMode(): 's' | 'w' {
        return document.body.classList.contains('app-wasm') ? 'w' : 's';
    };

    public static setRenderMode(mode: 'a' | 's' | 'w'): Promise<void> {
        return this.backendRef.invokeMethodAsync('SetRenderMode', mode) as unknown as Promise<void>;
    };

    public static showMicTroubleshooter(): void {
        void this.backendRef.invokeMethodAsync('ShowMicTroubleshooter');
    };

    public static showPhotoTroubleshooter(): void {
        void this.backendRef.invokeMethodAsync('ShowPhotoTroubleshooter');
    };

    public static showIncomingShareModal(): void {
        void this.backendRef.invokeMethodAsync('ShowIncomingShareModal');
    };

    /** Drives the recording quality controller through a synthetic
     *  -1 / 0 / +1 signal sweep over `period` seconds. ~10% of time at
     *  neutral, 45% at "drop", 45% at "raise". Verify via server logs:
     *  ChangeRecordingQuality calls should walk min↔max layer count. */
    public static testVideoRecordingQualityChange(period = 30): void {
        void this.backendRef.invokeMethodAsync('TestVideoRecordingQualityChange', period);
    }

    /** Drives the playback CapacityEstimator through the same -1 / 0 / +1
     *  sweep. Pushes ChangePlaybackQuality info-only payloads (no actual
     *  receive-quality changes); verify via server logs. */
    public static testVideoPlaybackQualityChange(period = 30): void {
        void this.backendRef.invokeMethodAsync('TestVideoPlaybackQualityChange', period);
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

    private static _rotateTimer: number | null = null;

    public static killVideoRecording(avgPeriod: VideoTraceKillPeriodInput = 10, killStage: number | string = 3): boolean {
        return this.setVideoTraceKill('recording', avgPeriod, killStage);
    }

    public static killVideoPlayback(avgPeriod: VideoTraceKillPeriodInput = 10, killStage: number | string = 63): boolean {
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

    /** On-demand: toggles the VirtualList consistency checker (see virtual-list-debug.ts).
     *  When on, every live list checks its geometry ~10×/s plus on data-request and render,
     *  logging inconsistencies and accumulating them in globalThis.__vlDebugs[identity]. */
    public static virtualListDebug(enable = true): void {
        const vl = (globalThis as Record<string, unknown>).VirtualList as
            { setDebugEnabled?: (e: boolean) => unknown } | undefined;
        if (!vl?.setDebugEnabled) {
            console.warn('virtualListDebug: VirtualList is not loaded yet');
            return;
        }
        vl.setDebugEnabled(enable);
        infoLog?.log(`virtualListDebug: ${enable ? 'enabled' : 'disabled'}`);
    }

    /** Returns accumulated VirtualList consistency violations across all live lists.
     *  Pass clear=true to also drain each list's buffer (so a poller doesn't re-report them). */
    public static listVirtualListViolations(clear = false): unknown[] {
        const reg = ((globalThis as Record<string, unknown>).__vlDebugs ?? {}) as
            Record<string, { violations?: unknown[]; clear?: () => void }>;
        const all: unknown[] = [];
        for (const d of Object.values(reg)) {
            if (d.violations)
                all.push(...d.violations);
            if (clear)
                d.clear?.();
        }
        return all;
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

    public static startFusionMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartFusionMonitor');
    };

    public static startTaskMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartTaskMonitor');
    };

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
            if (recentEvents.length > MAX) recentEvents.shift();
        };

        const snapshot = (): Record<string, unknown>[] => JSON.parse(JSON.stringify(recentEvents)) as Record<string, unknown>[];

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
            Codec: 'avc1.640028',
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
            try { stream.disconnect(); } catch { /* ignore */ }
        }
    }
}
