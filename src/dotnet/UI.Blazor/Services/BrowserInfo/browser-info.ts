import { PromiseSource } from 'promises';
import { Log, LogLevel } from 'logging';
import { audioContextLazy } from 'audio-context-lazy';
import { take } from 'rxjs';
import screenSize from '../ScreenSize/screen-size';

const LogScope = 'BrowserInfo';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class BrowserInfo {
    private static backendRef: DotNet.DotNetObject = null;
    private static _isMaui: boolean = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();
    public static utcOffset: number;
    public static isTouchCapableCached: boolean = null;
    public static windowId: string = "";

    public static init(backendRef1: DotNet.DotNetObject, isMaui: boolean): BrowserInfo {
        this.backendRef = backendRef1;
        this.utcOffset = new Date().getTimezoneOffset();
        // @ts-ignore
        this.windowId = window.App.windowId;
        this.whenReady.resolve(undefined);
        this._isMaui = isMaui;
        if (isMaui) {
            audioContextLazy.doNotWaitForInteraction();
        }

        screenSize.size
            .pipe(take(1))
            .subscribe(size => {
                const initResult: InitResult = {
                    screenSizeText: size,
                    utcOffset: this.utcOffset,
                    isTouchCapable: this.isTouchCapable,
                    windowId: this.windowId,
                };

                void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
                screenSize.size.subscribe(x => this.onScreenSizeChanged(x))
            });

        return this;
    }

    public static get isTouchCapable(): boolean {
        this.isTouchCapableCached ??=
            ( 'ontouchstart' in window )
            || ( navigator.maxTouchPoints > 0 )
            // @ts-ignore
            || ( navigator.msMaxTouchPoints > 0 );
        return this.isTouchCapableCached;
    }

    public static get isMaui(): boolean {
        return this._isMaui;
    }

    // Backend methods

    private static onScreenSizeChanged(screenSize: string): void {
        debugLog?.log(`onScreenSizeChanged, screenSize:`, screenSize);
        this.backendRef.invokeMethodAsync('OnScreenSizeChanged', screenSize)
    };
}

export interface InitResult {
    screenSizeText: string;
    utcOffset: number;
    isTouchCapable: boolean;
    windowId: string;
}
