import { DeviceInfo } from 'device-info';
import { PromiseSource } from 'promises';
import { Interactive } from 'interactive';
import { ScreenSize } from '../ScreenSize/screen-size';
import { Timeout } from 'timeout';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'BrowserInfo';
const log = Log.get(LogScope, LogLevel.Info);
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type AppKind = 'Unknown' | 'WebServer' | 'WasmApp' | 'MauiApp';

export class BrowserInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static appKind: AppKind;
    public static utcOffset: number;
    public static windowId: string = "";
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject, appKind: AppKind): void {
        this.backendRef = backendRef1;
        this.appKind = appKind;
        this.utcOffset = new Date().getTimezoneOffset();
        this.windowId = (globalThis['App'] as { windowId: string }).windowId;
        if (this.appKind == 'MauiApp')
            Interactive.isAlwaysInteractive = true;
        this.initBodyClasses();

        // Call OnInitialized
        const initResult: InitResult = {
            screenSizeText: ScreenSize.size,
            isHoverable: ScreenSize.isHoverable,
            utcOffset: this.utcOffset,
            isMobile: DeviceInfo.isMobile,
            isAndroid: DeviceInfo.isAndroid,
            isIos: DeviceInfo.isIos,
            isChrome: DeviceInfo.isChrome,
            isTouchCapable: DeviceInfo.isTouchCapable,
            windowId: this.windowId,
        };
        log?.log(`init:`, JSON.stringify(initResult));
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
        this.whenReady.resolve(undefined);

        ScreenSize.change$.subscribe(_ => this.onScreenSizeChanged(ScreenSize.size, ScreenSize.isHoverable))
        globalThis["browserInfo"] = this;
    }

    public static hardRedirect(url: string) {
        // We want this method to return immediately to report Blazor that it completed successfully
        Timeout.startRegular(50, () => window.location.assign(url));
    }

    // Backend methods

    private static onScreenSizeChanged(screenSize: string, isHoverable: boolean): void {
        log?.log(`onScreenSizeChanged, screenSize:`, screenSize);
        this.backendRef.invokeMethodAsync('OnScreenSizeChanged', screenSize, isHoverable)
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
        if (DeviceInfo.isChrome)
            classList.add('device-chrome');

        if (DeviceInfo.isTouchCapable)
            classList.add('touch-capable');
        else
            classList.add('touch-incapable');
    }
}

export interface InitResult {
    screenSizeText: string;
    isHoverable: boolean,
    utcOffset: number;
    isMobile: boolean;
    isAndroid: boolean;
    isIos: boolean;
    isChrome: boolean;
    isTouchCapable: boolean;
    windowId: string;
}
