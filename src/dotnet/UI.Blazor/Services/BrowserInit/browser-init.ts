import { EventHandlerSet } from "event-handling";
import { delayAsync, PromiseSource } from 'promises';
import { Log } from "logging";
import { AppKind, BrowserInfo } from "../BrowserInfo/browser-info";
import { isOnline, reloadCurrentPage, whenOnline } from 'connectivity';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('BrowserInit');

const window = globalThis as undefined as Window;
const sessionStorage = window.sessionStorage;

export class BrowserInit {
    public static apiVersion = "";
    public static baseUri = "";
    public static sessionHash = "";
    public static windowId = "";
    public static readonly isMauiApp = document.body.classList.contains('app-maui');
    public static readonly whenInitialized = new PromiseSource<void>();
    public static readonly whenReloading = new PromiseSource<void>();
    public static readonly reconnectedEvents = new EventHandlerSet<void>();
    public static connectionState = "";
    public static reconnectingPromise: Promise<void> = null;

    public static async init(
        appKind: AppKind,
        apiVersion: string,
        baseUri: string,
        sessionHash: string,
        browserInfoBackendRef: DotNet.DotNetObject,
    ): Promise<void> {
        try {
            infoLog?.log(`-> init, apiVersion: ${apiVersion}, baseUri: ${baseUri}, sessionHash: ${sessionHash}`);
            this.apiVersion = apiVersion;
            this.baseUri = baseUri;
            this.sessionHash = sessionHash;
            this.initWindowId();
            this.initAndroid();
            // this.preventSuspend();
            await BrowserInfo.init(browserInfoBackendRef, appKind);
        }
        catch (e) {
            errorLog?.log('init: error:', e);
            this.whenInitialized.reject(e);
            // We can't do much in this case, so...
            this.startReloading();
        }
        finally {
            this.whenInitialized.resolve(undefined);
            infoLog?.log('<- init');
        }
    }

    public static isAlive() : boolean {
        return this.apiVersion.length > 0;
    }

    public static getUrl(url: string) : string {
        // @ts-ignore
        const baseUri = BrowserInit.baseUri;
        return baseUri ? new URL(url, baseUri).toString() : url;
    }

    public static resetAppConnectionState(): void {
        if (this.connectionState === "")
            return; // Already reset

        this.setAppConnectionState();
        this.reconnectedEvents.triggerSilently();
    }

    public static startReconnecting(mustReconnectBlazor : boolean): void {
        this.setAppConnectionState("Reconnecting...");
        if (!mustReconnectBlazor)
            return;

        this.reconnectingPromise ??= (async (): Promise<void> => {
            try {
                const blazor = window['Blazor'];
                while (blazor) {
                    await whenOnline();
                    if (this.whenReloading.isCompleted())
                        return; // Already reloading

                    warnLog?.log('startReconnecting: reconnecting...');
                    try {
                        if (await blazor.reconnect())
                            return;
                    }
                    catch {
                        // Let's assume it may fail
                    }
                    errorLog?.log('startReconnecting: failed to reconnect');
                    if (await isOnline())
                        break; // Couldn't reconnect while online -> reload
                }
                this.startReloading();
            }
            finally {
                this.reconnectingPromise = null;
            }
        })();
    }

    public static startReloading(): void {
        if (this.whenReloading.isCompleted())
            return; // Already reloading

        if (!this.isMauiApp) // No "Reloading..." on MAUI app
            this.setAppConnectionState("Reloading...");

        warnLog?.log('startReloading: reloading...');
        this.whenReloading.resolve(undefined);
        void reloadCurrentPage();
    }

    public static startReloadWatchers() {
        const attachWatchers = () => {
            const errors = [];
            const reconnectDiv = document.getElementById('components-reconnect-modal');
            if (reconnectDiv) {
                const checkReconnectDiv = () => {
                    if (this.whenReloading.isCompleted())
                        return;

                    const classList = reconnectDiv.classList;
                    if (classList.length == 0 || classList.contains("components-reconnect-hide"))
                        this.resetAppConnectionState();
                    else if (classList.contains('components-reconnect-rejected'))
                        this.startReloading();
                    else if (classList.contains("components-reconnect-failed"))
                        this.startReconnecting(true);
                    else if (classList.contains("components-reconnect-show"))
                        this.startReconnecting(false);
                }
                const observer = new MutationObserver((mutations, _) => checkReconnectDiv());
                observer.observe(reconnectDiv, { attributes: true });
                checkReconnectDiv();
            }
            else
                errors.push('no reconnectDiv');

            const errorDiv = document.getElementById('blazor-error-ui');
            if (errorDiv) {
                const checkErrorDiv = () => {
                    if (errorDiv.style.display === 'block')
                        this.startReloading();
                }
                const observer = new MutationObserver((mutations, _) => checkErrorDiv());
                observer.observe(errorDiv, { attributes: true });
                checkErrorDiv();
            }
            else
                errors.push('no errorDiv');

            if (errors.length === 0)
                infoLog?.log(`startReloadWatchers: completed`);
            else
                errorLog?.log(`startReloadWatchers: errors:`, errors);
        }

        if (document.readyState === "loading")
            document.addEventListener("DOMContentLoaded", () => attachWatchers());
        else
            attachWatchers();
    }

    public static removeWebSplash(instantly = false) {
        document.body.style.backgroundColor = null;
        const overlay = document.getElementById('web-splash');
        if (!overlay)
            return;

        if (instantly) {
            overlay.remove();
            void BrowserInfo.onWebSplashRemoved();
        }
        else {
            overlay.classList.add('removing');
            // Total transition duration: 350ms, see loading-overlay.css
            setTimeout(function () {
                void BrowserInfo.onWebSplashRemoved();
                setTimeout(function () { overlay.remove(); }, 150);
            }, 200);
        }
    }

    public static async startWebSplashRemoval(delayMs: number): Promise<void> {
        await delayAsync(delayMs);
        this.removeWebSplash();
    }

    // Private methods

    private static initWindowId(): void {
        // Set App.windowId
        (() => {
            const windowIds = JSON
                .parse(sessionStorage.windowIds ?? "[]")
                .filter((value, i, a) => value != null);
            this.windowId = windowIds.pop();
            if (this.windowId == null)
                this.windowId = `${this.sessionHash}-${Math.random().toString(36).slice(2).substring(0, 6)}`;
            else
                sessionStorage.windowIds = JSON.stringify(windowIds);
        })();

        window.addEventListener("beforeunload", _ => {
            const windowIds = JSON.parse(sessionStorage.windowIds ?? "[]");
            windowIds.push(this.windowId);
            sessionStorage.windowIds = JSON.stringify(windowIds);
            return null;
        });
    }

    private static initAndroid(): void {
        const android = window['Android'] as unknown;
        if (!android)
            return;

        // In Android WebView readText and writeText operations fail with insufficient permissions,
        // and there is no way to grant these permissions.
        // https://stackoverflow.com/questions/61243646/clipboard-api-call-throws-notallowederror-without-invoking-onpermissionrequest
        // So we replace `navigator.clipboard` functions with our own implementation
        // based on JS-to-Native-Android interop.
        navigator.clipboard.writeText = clipText => {
            return new Promise((resolve, reject) => {
                try {
                    // @ts-ignore
                    android.writeTextToClipboard(clipText);
                    resolve();
                }
                catch (e) {
                    reject(e);
                }
            });
        };
        navigator.clipboard.readText = () => {
            return new Promise((resolve, reject) => {
                try {
                    // @ts-ignore
                    const clipText = android.readTextFromClipboard();
                    resolve(clipText);
                }
                catch (e) {
                    reject(e);
                }
            });
        };
    }

    private static preventSuspend(): void {
        const keepWebLock = async (): Promise<void> => {
            const lockId = `${this.windowId}-${Math.random()}`;
            // noinspection InfiniteLoopJS
            while (true) {
                try {
                    await navigator.locks.request(lockId, async () => {
                        debugLog?.log(`preventSuspend: lock acquired:`, lockId)
                        // noinspection InfiniteLoopJS
                        while (true) {
                            await delayAsync(3600_000); // 1h
                        }
                    });
                }
                catch {
                    // Intended
                }
                debugLog?.log(`preventSuspend: lock is lost`)
                await delayAsync(5_000); // 5s to retry
            }
        }

        void keepWebLock();
    }

    private static setAppConnectionState(state: string = ""): void {
        if (this.connectionState === state)
            return;

        this.connectionState = state;
        if (this.whenReloading.isCompleted())
            return;

        const appConnectionStateDiv = document.getElementById('app-connection-state');
        if (!appConnectionStateDiv)
            return;

        if (state) {
            appConnectionStateDiv.innerHTML = `
                <div class="c-bg"></div>
                <div class="c-circle-blur"></div>
                <div class="c-circle">
                    <!-- Must have open and close tags, otherwise doesn't work! -->
                    <loading-cat-svg></loading-cat-svg>
                    <span class="c-text">${state}</span>
                </div>
            `;
            appConnectionStateDiv.style.display = '';
        }
        else {
            appConnectionStateDiv.innerHTML = '';
            appConnectionStateDiv.style.display = 'none';
        }
    }
}

// This call must be done as soon as possible
BrowserInit.startReloadWatchers();
void BrowserInit.startWebSplashRemoval(5_000);
