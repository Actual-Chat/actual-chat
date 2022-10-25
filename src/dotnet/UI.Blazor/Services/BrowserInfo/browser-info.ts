import './browser-info.css'
import { PromiseSource } from 'promises';
import { Log, LogLevel } from 'logging';

const LogScope = 'BrowserInfo';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class BrowserInfo {
    private static screenSizeMeasureDiv: HTMLDivElement = null;
    private static backendRef: DotNet.DotNetObject = null;
    private static _isMaui: boolean = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();
    public static screenSize: string;
    public static utcOffset: number;
    public static isTouchCapableCached: boolean = null;
    public static windowId: string = "";

    public static init(backendRef1: DotNet.DotNetObject, isMaui: boolean): InitResult {
        this.backendRef = backendRef1;
        this.screenSizeMeasureDiv = document.createElement("div");
        this.screenSizeMeasureDiv.className = "screen-size-measure";
        document.body.appendChild(this.screenSizeMeasureDiv);
        this.screenSizeMeasureDiv.innerHTML = `
            <div data-size="ExtraLarge2"></div>
            <div data-size="ExtraLarge"></div>
            <div data-size="Large"></div>
            <div data-size="Medium"></div>
            <div data-size="Small"></div>
        `
        window.addEventListener('resize', this.onWindowResize);
        this.utcOffset = new Date().getTimezoneOffset();
        this.screenSize = this.measureScreenSize();
        // @ts-ignore
        this.windowId = window.App.windowId;
        this.whenReady.resolve(undefined);
        this._isMaui = isMaui;

        return {
            screenSizeText: this.screenSize,
            utcOffset: this.utcOffset,
            isTouchCapable: this.isTouchCapable,
            windowId: this.windowId,
        }
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

    // Event handlers

    private static onWindowResize = (event: Event) => {
        const screenSize = this.measureScreenSize();
        if (screenSize === this.screenSize)
            return;

        this.screenSize = screenSize;
        this.onScreenSizeChanged(this.screenSize);
    };

    // Private methods

    private static measureScreenSize(): string {
        let itemDiv : HTMLDivElement = null;
        for (const item of this.screenSizeMeasureDiv.children) {
            itemDiv = item as HTMLDivElement;
            if (!item)
                continue;

            const isVisible = window.getComputedStyle(itemDiv).getPropertyValue('width') !== 'auto';
            debugLog?.log(`measureScreenSize: size:`, itemDiv.dataset['size'], ', isVisible:', isVisible);
            if (isVisible)
                return itemDiv.dataset['size'];
        }
        // Returning the last "available" size
        return itemDiv.dataset['size'] ?? "Unknown";
    };
}

export interface InitResult {
    screenSizeText: string;
    utcOffset: number;
    isTouchCapable: boolean;
    windowId: string;
}
