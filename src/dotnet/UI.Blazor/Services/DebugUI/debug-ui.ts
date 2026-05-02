import { getLogs } from 'logging';
import { Api, WorkerKind } from 'api';
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

export class DebugUI {
    private static backendRef: DotNet.DotNetObject = null!;
    private static _eventSnifferInstalled = false;
    private static _audioRecorderOffsetHandler: ((offsetMs: number) => void) | null = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        infoLog?.log(`init`);
        this.backendRef = backendRef1;
        globalThis.debugUI = this;
    }

    public static startFusionMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartFusionMonitor');
    };

    public static startTaskMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartTaskMonitor');
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

    public static navigateTo(url: string): void {
        void this.backendRef.invokeMethodAsync('NavigateTo', url);
    };

    public static disconnectBlazorRpc(): void {
        void this.backendRef.invokeMethodAsync('DisconnectRpc');
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

    public static resetOnboarding(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetOnboarding', enable);
    };

    public static resetBubbles(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetBubbles', enable);
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

    public static fakeSleep(duration = 5): void {
        OnDeviceAwake.fakeSleep(duration * 1000);
    }

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

    /** OpusMediaRecorder registers a handler at init time. Stored here so
     *  DebugUI doesn't need to import the higher-level recorder module. */
    public static registerAudioRecorderOffsetHandler(handler: (offsetMs: number) => void): void {
        this._audioRecorderOffsetHandler = handler;
    }

    /** Debug-only: forces the audio recorder to add `offsetMs` ms to the
     *  source-time stamp (`clientStartOffset` / `BeginsAt`) it sends with
     *  every new PushStream. Lets us simulate audio drift for the catch-up
     *  policy. Pass 0 to clear. */
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
}
