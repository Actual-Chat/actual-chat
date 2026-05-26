// TODO: fix eslint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-unsafe-argument */
import { Api, streamingApi, uploadsApi } from 'api';
import { ConnectivityUI } from '../ConnectivityUI/connectivity-ui';
import { EventHandlerSet } from 'event-handling';
import { delayAsync, PromiseSource } from 'actuallab-core';
import { AppKind, BrowserInfo, HostKind } from '../BrowserInfo/browser-info';
import { getLogs } from 'logging';
import { MainThreadDiagnostics } from 'main-thread-diagnostics';
import { FirebaseApp, initializeApp } from 'firebase/app';
import { Analytics, getAnalytics, setAnalyticsCollectionEnabled } from 'firebase/analytics';
import { SessionTokens } from '../Security/session-tokens';
import { Versioning } from 'versioning';
import { initAppConstants, AppConstants } from 'app-constants';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('BrowserInit');
const IsAnalyticsEnabledSetting = 'isAnalyticsEnabled';

const sessionStorage = globalThis?.sessionStorage;

export class BrowserInit {
    public static apiVersion = '';
    public static baseUri = '';
    public static sessionHash = '';
    public static windowId = '';
    public static firebaseApp?: FirebaseApp;
    public static firebaseAnalytics?: Analytics;
    public static firebasePublicKey?: string;
    public static readonly isMauiApp = ConnectivityUI.isMauiApp;
    public static readonly whenInitialized = new PromiseSource<void>();
    public static readonly whenReloading = new PromiseSource<void>();
    public static readonly reconnectedEvents = new EventHandlerSet<void>();
    public static connectionState = '';
    public static reconnectingPromise: Promise<void> = null!;

    public static init(
        hostKind: HostKind,
        appKind: AppKind,
        apiVersion: string,
        baseUri: string,
        supportedHosts: string[],
        sessionHash: string,
        appConstants: AppConstants,
        browserInfoBackendRef: DotNet.DotNetObject,
        clipboardInteropRef: DotNet.DotNetObject | null,
    ): void {
        try {
            infoLog?.log(`-> init, apiVersion: ${apiVersion}, baseUri: ${baseUri}, sessionHash: ${sessionHash}`);
            initAppConstants(appConstants);
            if (!BrowserInit.isProdBaseUri(baseUri))
                MainThreadDiagnostics.init();
            window.App?.markBlazorReady?.(); // It must be called no matter what at this point
            this.apiVersion = apiVersion;
            const documentBaseUri = new URL(document.baseURI);
            this.baseUri = supportedHosts.includes(documentBaseUri.host) ? `${documentBaseUri.protocol}//${documentBaseUri.host}` : baseUri;
            Api.init('MainThread', {
                url: this.getUrl('/rpc/ws').replace(/^http/, 'ws'),
                modules: [streamingApi, uploadsApi],
                connectivityUI: ConnectivityUI,
                sessionTokenProvider: minLifespanMs => SessionTokens.get(minLifespanMs),
            });
            this.sessionHash = sessionHash;
            this.initWindowId();
            this.initClipboardHandlers(clipboardInteropRef);
            if (hostKind !== 'MauiApp')
                void this.initFirebase();

            // this.preventSuspend();
            BrowserInfo.init(browserInfoBackendRef, hostKind, appKind);
        }
        catch (e) {
            errorLog?.log('init: error:', e);
            this.whenInitialized.reject(e);
            // We can't do much in this case, so...
            void this.startReloading();
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
        const baseUri = BrowserInit.baseUri;
        return baseUri ? new URL(url, baseUri).toString() : url;
    }

    public static resetAppConnectionState(): void {
        if (this.connectionState === '')
            return; // Already reset

        this.setAppConnectionState();
        this.reconnectedEvents.triggerSilently();
    }

    public static startReconnecting(mustReconnectBlazor : boolean): void {
        this.setAppConnectionState('Reconnecting...');
        if (!mustReconnectBlazor)
            return;

        this.reconnectingPromise ??= (async (): Promise<void> => {
            try {
                const blazor = window['Blazor'] as { reconnect: () => Promise<boolean> };
                if (!blazor?.reconnect) {
                    // No Blazor reconnect = it's WASM & something is severely wrong,
                    // so the best we can do is to reload the page.
                    void this.startReloading();
                    return;
                }

                await ConnectivityUI.whenReadyToReload('reconnecting');
                if (this.whenReloading.isCompleted)
                    return; // Already reloading

                warnLog?.log('startReconnecting: reconnecting...');
                try {
                    if (await blazor.reconnect())
                        return;
                }
                catch {
                    // We assume it may fail
                }

                errorLog?.log('startReconnecting: failed to reconnect, will reload...');
                void this.startReloading();
            }
            finally {
                this.reconnectingPromise = null!;
            }
        })();
    }

    public static async startReloading(): Promise<void> {
        if (this.whenReloading.isCompleted)
            return; // Already reloading

        if (!this.isMauiApp) // No "Reloading..." on MAUI app
            this.setAppConnectionState('Reloading...');

        warnLog?.log('startReloading: reloading...');
        this.whenReloading.resolve(undefined);

        // @ts-expect-error because of the `window.opusMediaRecorder`
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
        await window?.opusMediaRecorder?.stop();

        await ConnectivityUI.whenReadyToReload('reloading');
        window.location.reload();
    }

    public static startReloadWatchers() {
        const attachWatchers = () => {
            const errors: string[] = [];
            const reconnectDiv = document.getElementById('components-reconnect-modal');
            if (reconnectDiv) {
                const checkReconnectDiv = () => {
                    if (this.whenReloading.isCompleted)
                        return;

                    const classList = reconnectDiv.classList;
                    if (classList.length == 0 || classList.contains('components-reconnect-hide'))
                        this.resetAppConnectionState();
                    else if (classList.contains('components-reconnect-rejected')) {
                        warnLog?.log(
                            'startReloading: triggered by #components-reconnect-modal',
                            { classes: reconnectDiv.className, text: reconnectDiv.innerText?.trim().slice(0, 300) });
                        void this.startReloading();
                    }
                    else if (classList.contains('components-reconnect-failed'))
                        this.startReconnecting(true);
                    else if (classList.contains('components-reconnect-show'))
                        this.startReconnecting(false);
                }
                const observer = new MutationObserver(() => checkReconnectDiv());
                observer.observe(reconnectDiv, { attributes: true });
                checkReconnectDiv();
            }
            else
                errors.push('no reconnectDiv');

            const errorDiv = document.getElementById('blazor-error-ui');
            if (errorDiv) {
                const checkErrorDiv = () => {
                    if (errorDiv.style.display === 'block') {
                        warnLog?.log(
                            'startReloading: triggered by #blazor-error-ui',
                            { text: errorDiv.innerText?.trim().slice(0, 300) });
                        void this.startReloading();
                    }
                }
                const observer = new MutationObserver(() => checkErrorDiv());
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

        if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', () => attachWatchers());
        else
            attachWatchers();
    }

    public static removeWebSplash(instantly = false) {
        document.body.style.backgroundColor = '';
        const splash = document.getElementById('web-splash');
        if (!splash)
            return;

        if (instantly) {
            splash.remove();
            void BrowserInfo.onWebSplashRemoved();
        }
        else {
            splash.classList.add('removing');
            // Total transition duration: 350ms, see web-splash.css
            setTimeout(function () {
                void BrowserInfo.onWebSplashRemoved();
                setTimeout(function () { splash.remove(); }, 150);
            }, 200);
        }
    }

    public static async startWebSplashRemoval(delayMs: number): Promise<void> {
        await delayAsync(delayMs);
        this.removeWebSplash();
    }

    /** Called from Blazor */
    public static async initFirebase(isAnalyticsEnabled: boolean | null = null): Promise<FirebaseApp | null> {
        if (isAnalyticsEnabled == null) {
            isAnalyticsEnabled = readSettingToggle(IsAnalyticsEnabledSetting);
        }
        else {
            persistSettingToggle(IsAnalyticsEnabledSetting, isAnalyticsEnabled);
        }
        if (BrowserInit.firebaseAnalytics && BrowserInit.firebasePublicKey && isAnalyticsEnabled !== null) {
            const analytics = BrowserInit.firebaseAnalytics;
            setAnalyticsCollectionEnabled(analytics, isAnalyticsEnabled);
            return analytics.app;
        }

        try {
            const firebaseConfigUrl = Versioning.mapPath('/dist/config/firebase.config.js');
            const response = await fetch(firebaseConfigUrl);
            if (response.ok || response.status === 304) {
                const { config, publicKey } = await response.json();
                const app = BrowserInit.firebaseApp = initializeApp(config, { automaticDataCollectionEnabled: isAnalyticsEnabled ?? false });
                BrowserInit.firebaseAnalytics = getAnalytics(app);
                BrowserInit.firebasePublicKey = publicKey;
                return app;
            }
            else {
                warnLog?.log(`initFirebase: unable to initialize firebase, status: ${response.status}`);
            }
        }
        catch (error) {
            errorLog?.log(`initFirebase: failed to initialize firebase app, error:`, error);
        }
        return null;
    }

    /** Called from Blazor */
    public static isFirebaseConfigured(): boolean {
        const isAnalyticsEnabled = readSettingToggle(IsAnalyticsEnabledSetting);
        return isAnalyticsEnabled !== null;
    }

    // Private methods

    private static initWindowId(): void {
        // Set App.windowId
        (() => {
            const windowIds = JSON
                .parse(sessionStorage?.windowIds ?? '[]')
                // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
                .filter((value) => value != null);
            // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
            this.windowId = windowIds.pop();
            if (this.windowId == null)
                this.windowId = `${this.sessionHash}-${Math.random().toString(36).slice(2).substring(0, 6)}`;
            else if (sessionStorage)
                sessionStorage.windowIds = JSON.stringify(windowIds);
        })();

        window.addEventListener('beforeunload', () => {
            const windowIds: string[] = JSON.parse(sessionStorage?.windowIds ?? '[]');
            windowIds.push(this.windowId);
            if (sessionStorage)
                sessionStorage.windowIds = JSON.stringify(windowIds);
            return null;
        });
    }

    private static initClipboardHandlers(clipboardHandlersRef: DotNet.DotNetObject | null): void {
        if (!clipboardHandlersRef)
            return;

        // In Android WebView, navigator.clipboard operations fail with insufficient permissions,
        // and there is no way to grant these permissions.
        // https://stackoverflow.com/questions/61243646/clipboard-api-call-throws-notallowederror-without-invoking-onpermissionrequest
        // We use Blazor JS interop (clipboardInteropRef) instead of the AndroidJSInterface
        // native bridge, because the latter suffers from CoreCLR garbage collecting
        // JNI callback delegates, causing native aborts on the JavaBridge thread.
        navigator.clipboard.writeText = async (clipText: string): Promise<void> => {
            await clipboardHandlersRef.invokeMethodAsync('WriteText', clipText);
        };
        navigator.clipboard.readText = async (): Promise<string> => {
            return await clipboardHandlersRef.invokeMethodAsync('ReadText') ?? '';
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

    private static setAppConnectionState(state = ''): void {
        if (this.connectionState === state)
            return;

        this.connectionState = state;
        if (this.whenReloading.isCompleted)
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

    private static isProdBaseUri(baseUri: string): boolean {
        try {
            const hostName = new URL(baseUri).hostname;
            return hostName === 'voxt.ai' || hostName === 'actual.chat';
        }
        catch {
            return false;
        }
    }
}

function persistSettingToggle(settingKey: string, value: boolean): boolean {
    if (!sessionStorage)
        return false;

    sessionStorage.setItem(settingKey, JSON.stringify(value));
    return true;
}

function readSettingToggle(settingKey: string): boolean | null {
    if (!sessionStorage)
        return null;

    const stringValue = sessionStorage.getItem(settingKey);
    if (stringValue == null)
        return null

    return JSON.parse(stringValue) as boolean | null;
}
