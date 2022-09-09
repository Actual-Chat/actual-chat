export class ScreenSizeProvider {
    private blazorRef: DotNet.DotNetObject;
    private readonly window: Window;
    private screenSizeLastValue : boolean;

    static create(blazorRef: DotNet.DotNetObject): ScreenSizeProvider {
        return new ScreenSizeProvider(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.window = document.defaultView;
        this.window.addEventListener('resize', this.windowResizeListener);
        this.screenSizeLastValue = this.isDesktopScreenSize();
        this.notifySizeChanged(this.screenSizeLastValue);
    }

    public isDesktopScreenSize = () : boolean => {
        const DesktopWidthThreshold : number = 1024;
        const width = this.window.innerWidth;
        return width >= DesktopWidthThreshold;
    };

    public dispose() {
        this.window.removeEventListener('resize', this.windowResizeListener);
    }

    private windowResizeListener = ((event: Event) => {
        // console.log("window size changed");
        const newScreenSize = this.isDesktopScreenSize();
        if (newScreenSize === this.screenSizeLastValue)
            return;
        this.screenSizeLastValue = newScreenSize;
        this.notifySizeChanged(newScreenSize);
    });

    private notifySizeChanged(isDesktop : boolean)
    {
        this.blazorRef.invokeMethodAsync('OnSizeChanged', isDesktop);
    }
}
