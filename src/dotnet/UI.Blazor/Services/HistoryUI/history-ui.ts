import { Log, LogLevel } from 'logging';
import { v4 as uuid } from 'uuid';

const LogScope: string = 'HistoryUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

type HistoryStep = {
    token: HistoryStepToken;
    index: number;
    data: any;
    onBack?: () => void | undefined
};

export class HistoryUI {
    private readonly historySteps: HistoryStep[];
    private stepIndex: number;
    private readonly _pushState :(data: any, unused: string, url?: string | URL | null) => void;
    private readonly _replaceState: (data: any, unused: string, url?: string | URL | null) => void;

    constructor() {
        this.stepIndex = 0;
        this.historySteps = [];
        this._pushState = history.pushState;
        this._replaceState = history.replaceState;
        this.SetupHistoryStateEnrichment();
        window.addEventListener('popstate', this.onPopState);
    }

    public pushBackStep(
        shouldBeReplaced : boolean,
        onBack?: () => void | undefined): HistoryStepToken
    {
        const state = history.state;
        const replace = state && state._shouldBeReplaced;
        const navigationOptions = {
            forceLoad: false,
            replaceHistoryEntry: replace,
        };
        this.internalNavigateTo(window.location.toString(), navigationOptions);
        if (shouldBeReplaced)
            history.state._shouldBeReplaced = true;
        const historyStep = this.historySteps[this.stepIndex];
        historyStep.onBack = onBack;
        return historyStep.token;
    }

    public isActiveStep(token : HistoryStepToken) : boolean
    {
        const historyStep = this.historySteps[this.stepIndex];
        return historyStep && token && historyStep.token == token;
    }

    private internalNavigateTo(uri: string, options: NavigationOptions): void
    {
        const blazor = (<any>window).Blazor;
        blazor._internal.navigationManager.navigateTo(uri, options);
    }

    private onPopState = (event : PopStateEvent) => {
        const prevStepIndex = this.stepIndex;
        this.stepIndex = this.getStepIndex(event.state);
        if (prevStepIndex === this.stepIndex)
            return;
        if (prevStepIndex > this.stepIndex) {
            debugLog.log(`Navigating back from ${prevStepIndex} to ${this.stepIndex}`);
            for (let i = prevStepIndex; i > this.stepIndex; i--) {
                const historyStep = this.historySteps[i];
                historyStep.onBack?.();
                delete this.historySteps[i];
            }
        }
        else {
            debugLog.log(`Navigating forward from ${prevStepIndex} to ${this.stepIndex}`);
        }
    }

    private getStepIndex = (data : any) : number =>
        data && data._stepIndex ? data._stepIndex as number : 0;

    private SetupHistoryStateEnrichment()
    {
        // Enrich history state that blazor setups
        // https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Services/NavigationManager.ts#L157
        // enrichment allows to detect navigation, back and forward moves.
        const historyUI = this;
        history.pushState = (data: any, unused: string, url?: string | URL | null) : void => {
            debugLog?.log(`pushState:`, data);
            const shouldBeReplaced = history.state && history.state._shouldBeReplaced;
            historyUI.navigate(!!shouldBeReplaced, data, unused, url);
        }
        history.replaceState = (data: any, unused: string, url?: string | URL | null) : void => {
            debugLog?.log(`replaceState:`, data);
            historyUI.navigate(true, data, unused, url);
        }
    }

    private enrichUserState(data: any)
    {
        return JSON.stringify(
            {
                ...data,
                _id: uuid(),
                userState: data?.userState,
            });
    };

    private navigate(replace : boolean, dataOriginal: any, unused: string, url?: string | URL | null)
    {
        const stepIndex = replace ? this.stepIndex : ++this.stepIndex;
        const data = {
            ...dataOriginal,
            _stepIndex : stepIndex,
            userState: this.enrichUserState(dataOriginal),
        };
        debugLog?.log(`onNavigate:`, replace, data, url);
        this.historySteps[stepIndex] = {
            index : stepIndex,
            data : data,
            token : {},
        };
        for (let i = stepIndex + 1; i < this.historySteps.length; i++) {
            delete this.historySteps[i];
        }
        if (replace)
            this._replaceState.call(history, data, unused, url);
        else
            this._pushState.call(history, data, unused, url);
    }
}

// Keep in sync with Components/src/NavigationOptions.cs
export interface NavigationOptions {
    forceLoad: boolean;
    replaceHistoryEntry: boolean;
    historyEntryState?: string;
}

export interface HistoryStepToken
{
}

const historyUI = new HistoryUI();
window['App'].historyUI = historyUI;
