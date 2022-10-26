import { Log, LogLevel } from 'logging';

const LogScope: string = 'HistoryUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class HistoryUI {
    static create(): HistoryUI {
        debugLog?.log(`create`);
        return new HistoryUI();
    }

    public getState = (): unknown => {
        const state = history.state;
        debugLog?.log(`getState:`, state);
        return state;
    }

    public setState = (state: unknown): void => {
        debugLog?.log(`setState:`, state);
        history.replaceState(state, '');
    }
}
