import {EventHandlerSet} from "event-handling";
import { delayAsync, PromiseSource } from 'promises';
import { Log } from "logging";

const { infoLog, warnLog, errorLog } = Log.get('BrowserInit');

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

    public static async init(apiVersion: string, baseUri: string, sessionHash: string, calls: Array<unknown>): Promise<void> {
        if (this.whenInitialized.isCompleted()) {
            errorLog?.log('init: already initialized, skipping');
            return;
        }

        try {
            infoLog?.log(`-> init, apiVersion: ${apiVersion}, baseUri: ${baseUri}, sessionHash: ${sessionHash}`);
            this.apiVersion = apiVersion;
            this.baseUri = baseUri;
            this.sessionHash = sessionHash;
            this.initWindowId();
            this.initAndroid();
            calls = Array.from(calls);
            const results = new Array<unknown>();
            for (let i = 0; i < calls.length;) {
                const name = calls[i] as string;
                const argumentCount = calls[i + 1] as number;
                const nextIndex = i + 2 + argumentCount;
                const args = calls.slice(i + 2, nextIndex);
                i = nextIndex;
                results.push(globalInvoke(name, args));

            }
            await Promise.all(results);
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

    public static resetAppConnectionState(): void {
        if (this.connectionState === "")
            return; // Already reset

        this.setAppConnectionState();
        this.reconnectedEvents.triggerSilently();
    }

    public static startReconnecting(mustReconnectBlazor): void {
        this.setAppConnectionState("Reconnecting...");
        if (mustReconnectBlazor) {
            const blazor = window['Blazor'];
            if (blazor)
                blazor.reconnect();
            else
                this.startReloading();
        }
    }

    public static startReloading(): void {
        if (this.whenReloading.isCompleted())
            return;

        if (!this.isMauiApp) // No "Reloading..." on MAUI app
            this.setAppConnectionState("Reloading...");
        this.whenReloading.resolve(undefined);
        window.setInterval(() => this.tryReload(), 2000);
        void this.tryReload();
    }

    public static startReloadWatchers() {
        const blazorReconnectDiv = document.getElementById('components-reconnect-modal');
        if (blazorReconnectDiv) {
            const observer = new MutationObserver((mutations, _) => {
                mutations.forEach(mutation => {
                    const target = mutation.target;
                    if (this.whenReloading.isCompleted() || !(target instanceof HTMLElement))
                        return;

                    const classList = target.classList;
                    if (classList.length == 0 || classList.contains("components-reconnect-hide"))
                        this.resetAppConnectionState();
                    else if (target.classList.contains('components-reconnect-rejected'))
                        this.startReloading();
                    else if (classList.contains("components-reconnect-failed"))
                        this.startReconnecting(true);
                    else if (classList.contains("components-reconnect-show"))
                        this.startReconnecting(false);
                });
            });
            observer.observe(blazorReconnectDiv, { attributes: true });
        }
        const blazorErrorDiv = document.getElementById('blazor-error-ui');
        if (blazorErrorDiv) {
            const observer = new MutationObserver((mutations, _) => {
                mutations.forEach(mutation => {
                    const target = mutation.target;
                    if (this.whenReloading.isCompleted() || !(target instanceof HTMLElement))
                        return;

                    if (target.style.display === 'block')
                        this.startReloading();
                });
            });
            observer.observe(blazorErrorDiv, { attributes: true });
        }
    }

    public static removeLoadingOverlay() {
        const overlay = document.getElementById('until-ui-is-ready');
        if (overlay)
            overlay.remove();
    }

    public static async startLoadingOverlayRemoval(delayMs: number): Promise<void> {
        await delayAsync(delayMs);
        this.removeLoadingOverlay();
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

        // In Android WebView read text and write text operations fail with insufficient permissions
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

    private static setAppConnectionState(state: string = ""): void {
        if (this.connectionState === state)
            return;

        this.connectionState = state;
        if (this.whenReloading.isCompleted())
            return;

        const appReloadingDiv = document.getElementById('app-connection-state');
        if (!appReloadingDiv)
            return;
        const textDiv = appReloadingDiv.querySelector('.c-text');
        if (!textDiv)
            return;

        if (state) {
            textDiv.innerHTML = state;
            appReloadingDiv.style.display = null;
        }
        else {
            appReloadingDiv.style.display = 'none';
        }
    }

    private static async tryReload(): Promise<void> {
        try {
            let response = await fetch('');
            if (response.ok)
                window.location.reload();
        }
        catch {
            warnLog?.log(`tryReload: waiting for connection to server to reload the app...`);
        }
    }
}

// This call must be done as soon as possible
BrowserInit.startReloadWatchers();
void BrowserInit.startLoadingOverlayRemoval(5_000);

// Helpers

function globalInvoke(name: string, args: unknown[]): any {
    const fn = globalEval(name) as Function;
    if (typeof fn === 'function') {
        const [typeName, methodName] = splitLast(name, '.');
        if (methodName === '') {
            infoLog?.log(`globalInvoke:`, name, ', arguments:', args);
            return fn(...args);
        }
        else {
            const self = globalEval(typeName);
            infoLog?.log(`globalInvoke:`, name, ', this:', self?.name, ', arguments:', args);
            return fn.apply(self, args);
        }
    }
    else {
        infoLog?.log(`globalInvoke: script:`, name);
        return fn as any;
    }
}

function globalEval(...args: any): any {
    return eval.apply(this, args);
}

function splitLast(source, by) {
    const lastIndex = source.lastIndexOf(by);
    if (lastIndex < 0)
        return [source, ''];

    const before = source.slice(0, lastIndex);
    const after = source.slice(lastIndex + 1);
    return [before, after];
}
