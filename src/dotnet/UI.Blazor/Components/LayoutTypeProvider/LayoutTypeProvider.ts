export class LayoutTypeProvider {
    private blazorRef: DotNet.DotNetObject;
    private readonly window: Window;
    private layoutLastValue : boolean;

    static create(blazorRef: DotNet.DotNetObject): LayoutTypeProvider {
        return new LayoutTypeProvider(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.window = document.defaultView;
        this.window.addEventListener('resize', this.windowResizeListener);
        this.layoutLastValue = this.isDesktopLayout();
        this.notifyLayoutChanged(this.layoutLastValue);
    }

    public isDesktopLayout = () : boolean => {
        const DesktopWidthThreshold : number = 1024;
        const width = this.window.innerWidth;
        return width >= DesktopWidthThreshold;
    };

    public dispose() {
        this.window.removeEventListener('resize', this.windowResizeListener);
    }

    private windowResizeListener = ((event: Event) => {
        // console.log("window size changed");
        const newLayout = this.isDesktopLayout();
        if (newLayout === this.layoutLastValue)
            return;
        this.layoutLastValue = newLayout;
        this.notifyLayoutChanged(newLayout);
    });

    private notifyLayoutChanged(isDesktop : boolean)
    {
        this.blazorRef.invokeMethodAsync('OnLayoutChanged', isDesktop);
    }
}
