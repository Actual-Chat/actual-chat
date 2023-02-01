import { DeviceInfo } from 'device-info';
import { PromiseSource } from 'promises';
import { Interactive } from 'interactive';
import { ScreenSize } from '../ScreenSize/screen-size';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'BrowserInfo';
const log = Log.get(LogScope, LogLevel.Info);
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type AppKind = 'Unknown' | 'WebServer' | 'Wasm' | 'Maui';

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
        this.initBodyClasses();

        // Call OnInitialized
        const initResult: InitResult = {
            screenSizeText: ScreenSize.size,
            utcOffset: this.utcOffset,
            isMobile: DeviceInfo.isMobile,
            isAndroid: DeviceInfo.isAndroid,
            isIos: DeviceInfo.isIos,
            isChrome: DeviceInfo.isChrome,
            isTouchCapable: DeviceInfo.isTouchCapable,
            windowId: this.windowId,
        };
        log?.log(`init:`, initResult);
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
        this.whenReady.resolve(undefined);

        ScreenSize.change$.subscribe(x => this.onScreenSizeChanged(x))
        if (this.appKind == 'Maui')
            Interactive.isAlwaysInteractive = true;
        globalThis["browserInfo"] = this;
    }

    public static redirect(url: string): void {
        log?.log(`redirect, url:`, url);
        this.backendRef.invokeMethodAsync('OnRedirect', url);
    };

    // Backend methods

    private static onScreenSizeChanged(screenSize: string): void {
        log?.log(`onScreenSizeChanged, screenSize:`, screenSize);
        this.backendRef.invokeMethodAsync('OnScreenSizeChanged', screenSize)
    };

    private static initBodyClasses() {
        const classList = document.body.classList;
        switch (this.appKind) {
        case 'WebServer':
            classList.add('app-web', 'app-server');
            break;
        case 'Wasm':
            classList.add('app-web', 'app-wasm');
            break;
        case 'Maui':
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
            classList.add('device-touch-capable');
    }
}

export interface InitResult {
    screenSizeText: string;
    utcOffset: number;
    isMobile: boolean;
    isAndroid: boolean;
    isIos: boolean;
    isChrome: boolean;
    isTouchCapable: boolean;
    windowId: string;
}
