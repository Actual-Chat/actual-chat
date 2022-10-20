const LogScope: string = 'HistoryUI';

export class HistoryUI {
    private blazorRef: DotNet.DotNetObject;

    static create(blazorRef: DotNet.DotNetObject): HistoryUI {
        console.debug(`${LogScope}: create`);
        return new HistoryUI(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        // Wiring up event listeners
        window.addEventListener('popstate', this.onPopState);
    }

    private onPopState = (async (event: PopStateEvent) => {
        console.debug(`${LogScope}: onPopState`);
        await this.blazorRef.invokeMethodAsync('OnPopState', JSON.stringify(event.state));
    });
}
