// TODO: fix eslint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-unsafe-argument */
import { Api, streamingApi, uploadsApi } from 'api';
import { AppRecovery } from '../AppRecovery/app-recovery';
import { ConnectivityUI } from '../ConnectivityUI/connectivity-ui';
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
const AppReadyDelayMs = 350;

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
    private static isAppReady = false;

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
            void AppRecovery.startReloading();
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

    public static removeWebSplash(instantly = false) {
        document.body.style.backgroundColor = '';
        const splash = document.getElementById('web-splash');
        if (!splash) {
            this.scheduleAppReady();
            return;
        }

        if (instantly) {
            splash.remove();
            void BrowserInfo.onWebSplashRemoved();
            this.scheduleAppReady();
        }
        else {
            splash.classList.add('removing');
            // Total transition duration: 350ms, see web-splash.css
            setTimeout(() => {
                void BrowserInfo.onWebSplashRemoved();
                setTimeout(() => {
                    splash.remove();
                    this.scheduleAppReady();
                }, 150);
            }, 200);
        }
    }

    public static async startWebSplashRemoval(timeoutMs: number): Promise<void> {
        // A WebView restart reloads the page against an already-warm .NET runtime, so the app can
        // paint within a few hundred ms - and the Blazor-side removal doesn't reliably land there.
        // Racing that paint against the timeout keeps the splash up only while it actually covers
        // something; the timeout is a backstop for the case where the app never renders at all.
        await Promise.race([this.whenAppRendered(), delayAsync(timeoutMs)]);
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

    private static whenAppRendered(): Promise<void> {
        const app = document.getElementById('app');
        if (app === null)
            return new PromiseSource<void>(); // Never completes - the caller's timeout decides
        if (app.childElementCount !== 0)
            return Promise.resolve();

        const whenRendered = new PromiseSource<void>();
        const observer = new MutationObserver(() => {
            if (app.childElementCount === 0)
                return;

            observer.disconnect();
            whenRendered.resolve(undefined);
        });
        observer.observe(app, { childList: true });
        return whenRendered;
    }

    // body.app-ready gates the composition-layer hints, the blurred-cover filter and the skeleton
    // pulse. Each layer costs the compositor a full-screen raster and each animation tick redraws
    // the whole screen on Android WebView, so before this class lands all of that would pile onto
    // the compositor exactly while it is the startup bottleneck.
    private static scheduleAppReady(): void {
        if (this.isAppReady)
            return;

        this.isAppReady = true;
        setTimeout(() => document.body.classList.add('app-ready'), AppReadyDelayMs);
    }

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
        // Route the rich (ClipboardItem) write through the native handler too — it stores HTML via
        // ClipData.newHtmlText, so our data-voxt-markup payload survives on the system clipboard.
        navigator.clipboard.write = async (items: ClipboardItem[]): Promise<void> => {
            const item = items?.[0];
            let text = '', html = '';
            if (item) {
                if (item.types.includes('text/plain'))
                    text = await (await item.getType('text/plain')).text();
                if (item.types.includes('text/html'))
                    html = await (await item.getType('text/html')).text();
            }
            await clipboardHandlersRef.invokeMethodAsync('WriteRichText', text, html);
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
