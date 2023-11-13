import { EventHandlerSet } from "event-handling";
import { delayAsync, PromiseSource } from 'promises';
import { Log } from "logging";
import { AudioRecorder } from "../../../Audio.UI.Blazor/Components/AudioRecorder/audio-recorder";
import { AudioPlayer } from "../../../Audio.UI.Blazor/Components/AudioPlayer/audio-player";
import { audioContextSource } from "../../../Audio.UI.Blazor/Services/audio-context-source";
import { DeviceInfo } from 'device-info';
import { AppKind, BrowserInfo } from "../BrowserInfo/browser-info";

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
    public static isTerminated = false;

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
        if (BrowserInit.isTerminated)
            return;

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
        if (BrowserInit.isTerminated)
            return;
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
                if (BrowserInit.isTerminated)
                    return;

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
                if (BrowserInit.isTerminated)
                    return;

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
        if (overlay) {
            overlay.style.opacity = '0';
            setTimeout(function() { overlay.remove(); }, 500);
        }
    }

    public static async startLoadingOverlayRemoval(delayMs: number): Promise<void> {
        await delayAsync(delayMs);
        this.removeLoadingOverlay();
    }

    public static async reload(): Promise<void> {
        // Force stop recording before reload
        warnLog?.log('reloading...');
        await globalThis['opusMediaRecorder']?.stop();
        if (BrowserInit.isTerminated)
            return;

        if (!window.location.hash) {
            // Refresh with GET
            // noinspection SillyAssignmentJS
            window.location.href = window.location.href;
        } else {
            window.location.reload();
        }
    }

    public static terminate(): void {
        // Force stop recording
        warnLog?.log('terminate()');
        BrowserInit.isTerminated  = true;

        void AudioRecorder.terminate();
        void AudioPlayer.terminate();
        void audioContextSource.terminate();

        // Clean up everything
        document.open();
        document.close();
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
                    <img draggable="false" src="/dist/images/loading-cat.svg" alt="">
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

    public static async tryReload(): Promise<void> {
        try {
            const response = await fetch('/favicon.ico');
            if (response.ok)
                void BrowserInit.reload();
        }
        catch {
            warnLog?.log(`tryReload: waiting for connection to server to reload the app...`);
        }
    }
}

// This call must be done as soon as possible
BrowserInit.startReloadWatchers();
void BrowserInit.startLoadingOverlayRemoval(5_000);
