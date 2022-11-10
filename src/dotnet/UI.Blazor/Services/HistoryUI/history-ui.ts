import { Log, LogLevel } from 'logging';
import { v4 as uuidv4 } from 'uuid';

const LogScope: string = 'HistoryUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class HistoryUI {
    static create(): HistoryUI {
        debugLog?.log(`create`);
        HistoryUI.SetupHistoryStateEnrichment();
        return new HistoryUI();
    }

    private static SetupHistoryStateEnrichment()
    {
        // Enrich history state that blazor setups
        // https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Services/NavigationManager.ts#L157
        // enrichment allows to detect navigation, back and forward moves.

        // enrich user state
        const enrichUserState = state =>
            JSON.stringify({
                               _index: state?._index ?? 0,
                               _id: uuidv4(),
                               userState: state.userState
                           });

        const pushState = history.pushState;
        history.pushState = function(state) {
            debugLog?.log(`pushState invoked:`, state);
            arguments[0] = {
                _index: state._index,
                userState: enrichUserState(state)
            };
            return pushState.apply(history, arguments);
        };

        const replaceState = history.replaceState;
        history.replaceState = function(state) {
            debugLog?.log(`replaceState invoked:`, state);
            arguments[0] = {
                _index: state._index,
                userState: enrichUserState(state)
            };
            return replaceState.apply(history, arguments);
        };
    }
}
