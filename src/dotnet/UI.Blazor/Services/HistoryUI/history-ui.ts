import { Log, LogLevel } from 'logging';
import { v4 as uuid } from 'uuid';

const LogScope: string = 'HistoryUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export type HistoryStep = {
    id: string;
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

let _nextStepId = 1;
let nextStepId = () => 'hs-' + (_nextStepId++).toString();

export class HistoryUI {
    private static readonly steps: Array<HistoryStep> = [{
        id: nextStepId(),
        index: 0,
        data: null,
    }]
    private static readonly _pushState = history.pushState;
    private static readonly _replaceState = history.replaceState;
    private static position: number = 0;

    public static get currentPosition(): number {
        return this.position;
    }
    public static get currentStepId(): string {
        return this.steps[this.position].id;
    }

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
    ): string {
        const state = history.state;
        const replace = state && state._shouldBeReplaced;
        const navigationOptions = {
            forceLoad: false,
            replaceHistoryEntry: replace,
        };
        this.internalNavigateTo(window.location.toString(), navigationOptions);
        if (shouldBeReplaced)
            history.state._shouldBeReplaced = true;
        const historyStep = this.steps[this.position];
        historyStep.onBack = onBack;
        return historyStep.id;
    }

    public static isCurrentStep(id: string): boolean
    {
        const step = this.steps[this.position];
        return step && id && step.id === id;
    }

    // Private methods

    private static internalNavigateTo(uri: string, options: NavigationOptions): void
    {
        const blazor = (<any>window).Blazor;
        blazor._internal.navigationManager.navigateTo(uri, options);
    }

    private static onPopState = (event: PopStateEvent) => {
        debugLog?.log(`onPopState. State: '${event.state ? JSON.stringify(event.state) : ''}'`);
        const oldPosition = this.position;
        this.position = this.getPosition(event.state) ?? 0;
        if (oldPosition === this.position)
            return; // No need to navigate

        if (oldPosition > this.position) {
            debugLog?.log(`onPopState: Navigating back: ${oldPosition} -> ${this.position}`);
            for (let i = oldPosition; i > this.position; i--) {
                const historyStep = this.steps[i];
                if (historyStep) {
                    historyStep.onBack?.();
                    this.steps.splice(i, 1);
                }
            }
        }
        else {
            debugLog?.log(`onPopState: Navigating forward: ${oldPosition} -> ${this.position}`);
        }
    }

    private static getPosition(data: any): number | null {
        let position = data?._stepIndex;
        if (typeof position !== 'number')
            return null;

        position = Math.min(position, this.steps.length - 1);
        position = Math.max(position, 0);
        return position;
    }

    private static navigate(replace: boolean, dataOriginal: any, unused: string, url?: string | URL | null)
    {
        const stepIndex = replace ? this.position : ++this.position;
        const data = {
            ...dataOriginal,
            _stepIndex: stepIndex,
            _id: uuid(),
        };
        data.userState = JSON.stringify(data);
        debugLog?.log(`onNavigate:`, replace, data, url);
        this.steps[stepIndex] = {
            id: nextStepId(),
            index: stepIndex,
            data: data,
        };
        this.steps.splice(stepIndex + 1);
        if (replace)
            this._replaceState.call(history, data, unused, url);
        else
            this._pushState.call(history, data, unused, url);
    }
}

HistoryUI.init();
