import { BrowserInit } from '../BrowserInit/browser-init';
import { DeviceInfo } from 'device-info';
import { delayAsync, PromiseSource } from 'actuallab-core';
import { Interactive } from 'interactive';
import { ScreenSize } from '../ScreenSize/screen-size';
import { getLogs } from 'logging';
import { DocumentEvents } from 'event-handling';
import { Theme, ThemeInfo } from 'theme';

const { infoLog } = getLogs('BrowserInfo');

export type HostKind = 'Unknown' | 'WebServer' | 'WasmApp' | 'MauiApp';
export type AppKind = 'Unknown' | 'Wasm' | 'Android' | 'Ios' | 'Windows' | 'MacOS';

export class BrowserInfo {
    private static backendRef: DotNet.DotNetObject = null!;
    private static isWebSplashRemoved: boolean;

    public static hostKind: HostKind = window.location.host === 'localhost' || window.location.host === '0.0.0.0' || window.location.host === '0.0.0.1'
        ? 'MauiApp'
        : ('MONO' in window)
            ? 'WasmApp'
            : 'WebServer';
    public static appKind: AppKind = 'Unknown';
    // eslint-disable-next-line
    public static renderMode = window?.['App']?.renderMode;
    public static utcOffset: number;
    public static timeZone: string;
    public static windowId = '';
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject, hostKind: HostKind, appKind: AppKind): void {
        Theme.changed.add(theme => this.onThemeChanged(theme));
        infoLog?.log(`initializing`);
        this.backendRef = backendRef1;
        this.hostKind = hostKind;
        this.appKind = appKind;
        this.utcOffset = new Date().getTimezoneOffset();
        this.timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        this.windowId = BrowserInit.windowId; // It is already computed when this call happens
        if (this.hostKind == 'MauiApp')
            Interactive.isAlwaysInteractive = true;
        this.initBodyClasses();
        globalThis.browserInfo = this;

        // Call OnInitialized
        const isWasmReady = this.isWasmReady();
        const initResult: InitResult = {
            screenSizeText: ScreenSize.size,
            isVisible: !document.hidden,
            isHoverable: ScreenSize.isHoverable,
            themeInfo: Theme.info!,
            utcOffset: this.utcOffset,
            timeZone: this.timeZone,
            isMobile: DeviceInfo.isMobile,
            isAndroid: DeviceInfo.isAndroid,
            isIos: DeviceInfo.isIos,
            isChromium: DeviceInfo.isChromium,
            isEdge: DeviceInfo.isEdge,
            isFirefox: DeviceInfo.isFirefox,
            isWebKit: DeviceInfo.isWebKit,
            isTouchCapable: DeviceInfo.isTouchCapable,
            isWasmReady: isWasmReady,
            windowId: this.windowId,
        };
        infoLog?.log(`init:`, JSON.stringify(initResult), hostKind);
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
        this.whenReady.resolve(undefined);

        ScreenSize.change$.subscribe(() => void this.onScreenSizeChanged(ScreenSize.size, ScreenSize.isHoverable));
        DocumentEvents.passive.visibilityChange$.subscribe(() => void this.onVisibilityChanged());
        if (isWasmReady === false)
            void this.startWasmReadyWatcher();
    }

    // Backend methods

    private static async onScreenSizeChanged(screenSize: string, isHoverable: boolean): Promise<void> {
        infoLog?.log(`onScreenSizeChanged, screenSize:`, screenSize);
        await this.whenReady;
        void this.backendRef.invokeMethodAsync('OnScreenSizeChanged', screenSize, isHoverable);
    };

    private static async onVisibilityChanged(): Promise<void> {
        infoLog?.log(`onVisibilityChanged, visible:`, !document.hidden);
        await this.whenReady;
        void this.backendRef.invokeMethodAsync('OnIsVisibleChanged', !document.hidden);
    };

    public static async onThemeChanged(themeInfo: ThemeInfo): Promise<void> {
        infoLog?.log(`onThemeChanged:`, themeInfo);
        await this.whenReady;
        void this.backendRef.invokeMethodAsync('OnThemeChanged', themeInfo);
    };

    public static async onWebSplashRemoved(): Promise<void> {
        if (this.isWebSplashRemoved)
            return;

        this.isWebSplashRemoved = true;
        infoLog?.log(`onWebSplashRemoved`);
        await this.whenReady;
        void this.backendRef.invokeMethodAsync('OnWebSplashRemoved');
    };

    public static async onWasmReady(): Promise<void> {
        infoLog?.log(`onWasmReady`);
        await this.whenReady;
        void this.backendRef.invokeMethodAsync('OnWasmReady');
    }

    // Other methods

    private static initBodyClasses() {
        const classList = document.body.classList;
        switch (this.hostKind) {
        case 'WebServer':
            classList.add('app-server');
            break;
        case 'WasmApp':
            classList.add('app-wasm');
            break;
        case 'MauiApp':
            classList.add('app-maui');
            break;
        default:
            classList.add('app-unknown');
            break;
        }
    }

    private static isWasmReady(): boolean | null {
        if (this.renderMode === 'w')
            return true;
        if (this.renderMode !== 'a')
            return null;

        const p = window.getComputedStyle(document.body).getPropertyValue('--blazor-load-percentage');
        return p === '100%';
    }

    private static async startWasmReadyWatcher(): Promise<void> {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        while (true) {
            await delayAsync(1000);
            if (this.isWasmReady() === true) {
                void this.onWasmReady();
                break;
            }
        }
    }
}

export interface InitResult {
    screenSizeText: string;
    isVisible: boolean,
    isHoverable: boolean,
    themeInfo: ThemeInfo,
    utcOffset: number;
    timeZone: string;
    isMobile: boolean;
    isAndroid: boolean;
    isIos: boolean;
    isChromium: boolean;
    isEdge: boolean;
    isFirefox: boolean;
    isWebKit: boolean;
    isTouchCapable: boolean;
    isWasmReady: boolean | null;
    windowId: string;
}
