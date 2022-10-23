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

    public getState = () : any => {
        const state = history.state;
        console.debug('getState: ', state);
        return state;
    }

    public setState = (state) => {
        console.debug('setState: ', state);
        history.replaceState(state, '');
    }

    private onPopState = (async (event: PopStateEvent) => {
        console.debug(`${LogScope}: onPopState`);
        await this.blazorRef.invokeMethodAsync('OnPopState', JSON.stringify(event.state));
    });
}
