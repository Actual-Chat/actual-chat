import { Log } from 'logging';

const { infoLog } = Log.get('DebugUI');

export class DebugUI {
    private static backendRef: DotNet.DotNetObject = null!;
    private static _eventSnifferInstalled = false;

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
        const settings = await this.backendRef.invokeMethodAsync("GetThreadPoolSettings");
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

    public static disconnectRpc(): void {
        void this.backendRef.invokeMethodAsync('DisconnectRpc');
    };

    public static resetOnboarding(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetOnboarding', enable);
    };

    public static resetBubbles(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetBubbles', enable);
    };

    public static startDOMEventSniffer(): void {
        if (this._eventSnifferInstalled) {
            infoLog?.log("startDOMEventSniffer: already installed");
            return;
        }
        this._eventSnifferInstalled = true;

        const recentEvents: Record<string, unknown>[] = [];
        const MAX = 50;

        const push = (entry: Record<string, unknown>) => {
            recentEvents.push(entry);
            if (recentEvents.length > MAX) recentEvents.shift();
        };

        const snapshot = () => JSON.parse(JSON.stringify(recentEvents));

        // 1. Blazor-level interception via pre-start hook
        let hasBlazorHook = false;
        const hook = (globalThis as any).__blazorEventSnifferHook as
            | ((cb: (desc: any, args: any) => void) => void)
            | undefined;
        if (hook) {
            hook((eventDescriptor, _eventArgs) => {
                const entry = {
                    time: new Date().toISOString().slice(11, 23),
                    src: "blazor",
                    eventName: eventDescriptor?.eventName,
                    handlerId: eventDescriptor?.eventHandlerId,
                };
                push(entry);
                console.debug("Blazor dispatch:", entry.eventName, "handlerId=" + entry.handlerId);
            });
            hasBlazorHook = true;
        }

        // 2. DOM-level capturing for context
        const TRACK = ["click", "mouseenter", "mouseleave", "pointerdown", "pointerup", "focusin", "focusout"];
        TRACK.forEach((type) => {
            document.addEventListener(
                type,
                (e) => {
                    const target = e.target as Element;
                    push({
                        time: new Date().toISOString().slice(11, 23),
                        src: "dom",
                        type: e.type,
                        tag: target?.tagName,
                        cls: (target?.className || "").toString().substring(0, 60),
                        key: target?.closest?.("[data-key]")?.getAttribute("data-key"),
                    });
                },
                true,
            );
        });

        // 3. Catch NullRef from Blazor's endInvokeDotNetFromJS
        const bi = (globalThis as any).Blazor?._internal;
        if (bi?.endInvokeDotNetFromJS) {
            const orig = bi.endInvokeDotNetFromJS;
            bi.endInvokeDotNetFromJS = function (asyncCallId: number, success: boolean, resultOrError: string) {
                if (
                    !success &&
                    typeof resultOrError === "string" &&
                    (resultOrError.includes("NullReferenceException") ||
                        resultOrError.includes("no event handler") ||
                        resultOrError.includes("DispatchEventAsync"))
                ) {
                    console.error(
                        "%c Blazor event dispatch failed! ",
                        "background:red;color:white;font-weight:bold;padding:2px 6px",
                        "\nCallId:",
                        asyncCallId,
                        "\nError:",
                        resultOrError.substring(0, 200),
                        "\nRecent events:",
                        snapshot(),
                    );
                }
                return orig.apply(this, arguments);
            };
        }

        // 4. Fallback: unhandled promise rejections
        window.addEventListener("unhandledrejection", (e) => {
            const msg =
                (e as PromiseRejectionEvent).reason?.message || (e as PromiseRejectionEvent).reason?.toString?.() || "";
            if (msg.includes("NullReferenceException")) {
                console.error(
                    "%c NullRef caught! ",
                    "background:red;color:white;font-weight:bold;padding:2px 6px",
                    "\nRecent events:",
                    snapshot(),
                );
            }
        });

        infoLog?.log(`startDOMEventSniffer: installed` + (hasBlazorHook ? "" : " (no Blazor hook)"));
    }
}
