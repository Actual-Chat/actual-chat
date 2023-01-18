import { PromiseSource } from 'promises';
import { Log, LogLevel } from 'logging';
import { ScreenSize } from '../ScreenSize/screen-size';
import { InteractiveUI } from '../InteractiveUI/interactive-ui';

const LogScope = 'BrowserInfo';
const log = Log.get(LogScope, LogLevel.Info);
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type AppKind = 'Unknown' | 'WebServer' | 'Wasm' | 'Maui';

export class BrowserInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static appKind: AppKind;
    public static utcOffset: number;
    public static isMobile: boolean;
    public static isTouchCapable: boolean;
    public static windowId: string = "";
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject, appKind: AppKind): void {
        this.backendRef = backendRef1;
        this.appKind = appKind;
        this.utcOffset = new Date().getTimezoneOffset();
        // @ts-ignore
        this.windowId = window.App.windowId;

        const userAgentData: { mobile?: boolean; } = self.navigator['userAgentData'] as { mobile?: boolean; };
        this.isMobile = userAgentData?.mobile
            // Additional check for browsers which don't support userAgentData
            ?? /Android|Mobile|Phone|webOS|iPhone|iPad|iPod|BlackBerry/i.test(self.navigator.userAgent);
        this.isTouchCapable =
            ( 'ontouchstart' in window )
            || ( navigator.maxTouchPoints > 0 )
            // @ts-ignore
            || ( navigator.msMaxTouchPoints > 0 );
        this.initBodyClasses();

        // Call OnInitialized
        const initResult: InitResult = {
            screenSizeText: ScreenSize.size,
            utcOffset: this.utcOffset,
            isMobile: this.isMobile,
            isTouchCapable: this.isTouchCapable,
            windowId: this.windowId,
        };
        log?.log(`init:`, initResult);
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
        this.whenReady.resolve(undefined);

        ScreenSize.change$.subscribe(x => this.onScreenSizeChanged(x))
        if (this.appKind == 'Maui')
            InteractiveUI.isAlwaysInteractive = true;
    }

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

        if (this.isMobile)
            classList.add('device-mobile');
        else
            classList.add('device-desktop');

        if (this.isTouchCapable)
            classList.add('device-touch-capable');
    }
}

export interface InitResult {
    screenSizeText: string;
    utcOffset: number;
    isMobile: boolean;
    isTouchCapable: boolean;
    windowId: string;
}
