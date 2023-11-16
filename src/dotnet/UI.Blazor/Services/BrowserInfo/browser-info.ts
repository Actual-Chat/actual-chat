import { BrowserInit } from '../BrowserInit/browser-init';
import { DeviceInfo } from 'device-info';
import { PromiseSource } from 'promises';
import { Interactive } from 'interactive';
import { ScreenSize } from '../ScreenSize/screen-size';
import { Log } from 'logging';
import { DocumentEvents } from 'event-handling';
import {Theme, ThemeInfo} from "theme";

const { infoLog } = Log.get('BrowserInfo');

export type AppKind = 'Unknown' | 'WebServer' | 'WasmApp' | 'MauiApp';

export class BrowserInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static appKind: AppKind;
    public static utcOffset: number;
    public static windowId = "";
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static async init(backendRef1: DotNet.DotNetObject, appKind: AppKind): Promise<void> {
        Theme.changed.add(theme => this.onThemeChanged(theme));
        infoLog?.log(`initializing`);
        this.backendRef = backendRef1;
        this.appKind = appKind;
        this.utcOffset = new Date().getTimezoneOffset();
        this.windowId = BrowserInit.windowId; // It is already computed when this call happens
        if (this.appKind == 'MauiApp')
            Interactive.isAlwaysInteractive = true;
        this.initBodyClasses();

        // Call OnInitialized
        const initResult: InitResult = {
            screenSizeText: ScreenSize.size,
            isVisible: !document.hidden,
            isHoverable: ScreenSize.isHoverable,
            themeInfo: Theme.info,
            utcOffset: this.utcOffset,
            isMobile: DeviceInfo.isMobile,
            isAndroid: DeviceInfo.isAndroid,
            isIos: DeviceInfo.isIos,
            isChromium: DeviceInfo.isChromium,
            isEdge: DeviceInfo.isEdge,
            isFirefox: DeviceInfo.isFirefox,
            isWebKit: DeviceInfo.isWebKit,
            isTouchCapable: DeviceInfo.isTouchCapable,
            windowId: this.windowId,
        };
        infoLog?.log(`init:`, JSON.stringify(initResult), appKind);
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
        this.whenReady.resolve(undefined);

        ScreenSize.change$.subscribe(_ => void this.onScreenSizeChanged(ScreenSize.size, ScreenSize.isHoverable));
        DocumentEvents.passive.visibilityChange$.subscribe(_ => void this.onVisibilityChanged());
        globalThis["browserInfo"] = this;
    }

    // Backend methods

    private static async onScreenSizeChanged(screenSize: string, isHoverable: boolean): Promise<void> {
        infoLog?.log(`onScreenSizeChanged, screenSize:`, screenSize);
        await this.whenReady;
        this.backendRef.invokeMethodAsync('OnScreenSizeChanged', screenSize, isHoverable);
    };

    private static async onVisibilityChanged(): Promise<void> {
        infoLog?.log(`onVisibilityChanged, visible:`, !document.hidden);
        await this.whenReady;
        this.backendRef.invokeMethodAsync('OnIsVisibleChanged', !document.hidden);
    };

    public static async onThemeChanged(themeInfo: ThemeInfo): Promise<void> {
        infoLog?.log(`onThemeChanged:`, themeInfo);
        await this.whenReady;
        this.backendRef.invokeMethodAsync('OnThemeChanged', themeInfo);
    };

    private static initBodyClasses() {
        const classList = document.body.classList;
        switch (this.appKind) {
        case 'WebServer':
            classList.add('app-web', 'app-server');
            break;
        case 'WasmApp':
            classList.add('app-web', 'app-wasm');
            break;
        case 'MauiApp':
            classList.add('app-mobile', 'app-maui');
            break;
        default:
            classList.add('app-unknown');
            break;
        }

        if (DeviceInfo.isMobile)
            classList.add('device-mobile');
        else
            classList.add('device-desktop');

        if (DeviceInfo.isAndroid)
            classList.add('device-android');
        if (DeviceInfo.isIos)
            classList.add('device-ios');
        if (DeviceInfo.isChromium)
            classList.add('device-chrome');
        if (DeviceInfo.isEdge)
            classList.add('device-edge');
        if (DeviceInfo.isWebKit)
            classList.add('device-webkit');

        if (DeviceInfo.isTouchCapable)
            classList.add('touch-capable');
        else
            classList.add('touch-incapable');
    }
}

export interface InitResult {
    screenSizeText: string;
    isVisible: boolean,
    isHoverable: boolean,
    themeInfo: ThemeInfo,
    utcOffset: number;
    isMobile: boolean;
    isAndroid: boolean;
    isIos: boolean;
    isChromium: boolean;
    isEdge: boolean;
    isFirefox: boolean;
    isWebKit: boolean;
    isTouchCapable: boolean;
    windowId: string;
}
