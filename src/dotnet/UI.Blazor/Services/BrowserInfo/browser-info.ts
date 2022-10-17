import './browser-info.css'

const LogScope: string = 'BrowserInfo';
const debug: boolean = true;

export class BrowserInfo {
    private static screenSizeMeasureDiv: HTMLDivElement = null;
    private static backendRef: DotNet.DotNetObject = null;
    private static isTouchCapableCached: boolean = null;
    private static windowId: string = "";

    public static screenSize: string;

    public static init(backendRef1: DotNet.DotNetObject): InitResult {
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
        this.screenSize = this.measureScreenSize();
        // @ts-ignore
        this.windowId = window.App.windowId;
        return {
            screenSizeText: this.screenSize,
            isTouchCapable: this.isTouchCapable(),
            windowId: this.windowId,
        }
    }

    public static isTouchCapable(): boolean {
        this.isTouchCapableCached ??=
            ( 'ontouchstart' in window )
            || ( navigator.maxTouchPoints > 0 )
            // @ts-ignore
            || ( navigator.msMaxTouchPoints > 0 );
        return this.isTouchCapableCached;
    }

    // Backend methods

    private static onScreenSizeChanged(screenSize: string): void {
        if (debug)
            console.debug(`${LogScope}.onScreenSizeChanged(${screenSize})`);
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
            // if (debug)
            //     console.debug(`${LogScope}.measureScreenSize:`, itemDiv.dataset['size'], isVisible);
            if (isVisible)
                return itemDiv.dataset['size'];
        }
        // Returning the last "available" size
        return itemDiv.dataset['size'] ?? "Unknown";
    };
}

export interface InitResult {
    screenSizeText: string;
    isTouchCapable: boolean;
    windowId: string;
}
