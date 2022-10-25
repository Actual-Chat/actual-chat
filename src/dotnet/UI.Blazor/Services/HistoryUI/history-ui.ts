const LogScope: string = 'HistoryUI';

export class HistoryUI {
    static create(): HistoryUI {
        console.debug(`${LogScope}: create`);
        return new HistoryUI();
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
}
