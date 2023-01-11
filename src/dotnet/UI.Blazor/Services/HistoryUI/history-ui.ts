import { Log, LogLevel } from 'logging';
import { v4 as uuid } from 'uuid';

const LogScope: string = 'HistoryUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export type HistoryStepId = number;

export type HistoryStep = {
    id: HistoryStepId;
    index: number;
    data: any;
    onBack?: () => void | undefined
};

// Keep in sync with Components/src/NavigationOptions.cs
export interface NavigationOptions {
    forceLoad: boolean;
    replaceHistoryEntry: boolean;
    historyEntryState?: string;
}

let nextHistoryStepId: HistoryStepId = 1; // !0 == !undefined (frequent check), so let's avoid it

export class HistoryUI {
    private static readonly historySteps: Array<HistoryStep> = [{
        id: nextHistoryStepId++,
        index: 0,
        data: null,
    }]
    private static readonly _pushState = history.pushState;
    private static readonly _replaceState = history.replaceState;
    private static stepIndex: number = 0;

    public static init(): void {
        // Enrich history state that blazor sets up
        // https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Services/NavigationManager.ts#L157
        // enrichment allows to detect navigation, back and forward moves.

        history.pushState = (data: any, unused: string, url?: string | URL | null): void => {
            debugLog?.log(`pushState:`, data);
            const shouldBeReplaced = history.state && history.state._shouldBeReplaced;
            HistoryUI.navigate(!!shouldBeReplaced, data, unused, url);
        }
        history.replaceState = (data: any, unused: string, url?: string | URL | null): void => {
            debugLog?.log(`replaceState:`, data);
            HistoryUI.navigate(true, data, unused, url);
        }
        window.addEventListener('popstate', this.onPopState);
    }

    public static pushBackStep(
        shouldBeReplaced: boolean,
        onBack?: () => void | undefined
    ): HistoryStepId
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
        return historyStep.id;
    }

    public static isActiveStep(id: HistoryStepId) : boolean
    {
        const historyStep = this.historySteps[this.stepIndex];
        return historyStep && id && historyStep.id == id;
    }

    // Private methods

    private static internalNavigateTo(uri: string, options: NavigationOptions): void
    {
        const blazor = (<any>window).Blazor;
        blazor._internal.navigationManager.navigateTo(uri, options);
    }

    private static onPopState = (event: PopStateEvent) => {
        const oldStepIndex = this.stepIndex;
        this.stepIndex = this.getStepIndex(event.state);
        if (oldStepIndex === this.stepIndex)
            return; // No need to navigate

        if (oldStepIndex > this.stepIndex) {
            debugLog?.log(`onPopState: Navigating back from ${oldStepIndex} to ${this.stepIndex}`);
            for (let i = oldStepIndex; i > this.stepIndex; i--) {
                const historyStep = this.historySteps[i];
                if (historyStep) {
                    historyStep.onBack?.();
                    this.historySteps.splice(i, 1);
                }
            }
        }
        else {
            debugLog?.log(`onPopState: Navigating forward from ${oldStepIndex} to ${this.stepIndex}`);
        }
    }

    private static getStepIndex(data: any): number {
        let stepIndex = data && data._stepIndex ? data._stepIndex as number : 0;
        stepIndex = Math.min(stepIndex, this.historySteps.length - 1);
        stepIndex = Math.max(stepIndex, 0);
        return stepIndex;
    }

    private static enrichUserState(data: any)
    {
        return JSON.stringify({
            ...data,
            _id: uuid(),
            userState: data?.userState,
        });
    };

    private static navigate(replace: boolean, dataOriginal: any, unused: string, url?: string | URL | null)
    {
        const stepIndex = replace ? this.stepIndex : ++this.stepIndex;
        const data = {
            ...dataOriginal,
            _stepIndex: stepIndex,
            userState: this.enrichUserState(dataOriginal),
        };
        debugLog?.log(`onNavigate:`, replace, data, url);
        this.historySteps[stepIndex] = {
            id: nextHistoryStepId++,
            index: stepIndex,
            data: data,
        };
        this.historySteps.splice(stepIndex + 1);
        if (replace)
            this._replaceState.call(history, data, unused, url);
        else
            this._pushState.call(history, data, unused, url);
    }
}

HistoryUI.init();
