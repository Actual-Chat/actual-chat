import { fromEvent, Subject, takeUntil } from 'rxjs';
import { stopEvent } from 'event-handling';

import { Log } from 'logging';
const { debugLog } = Log.get('LandingLeftMenu');

export class LandingLeftMenu {
    private readonly disposed$ = new Subject<void>();
    private readonly _ref: HTMLElement;
    private readonly _blazorRef: DotNet.DotNetObject;

    static create(docs: HTMLElement, blazorRef: DotNet.DotNetObject): LandingLeftMenu {
        return new LandingLeftMenu(docs, blazorRef);
    }

    constructor(
        ref: HTMLElement,
        blazorRef: DotNet.DotNetObject,
    ) {
        debugLog?.log('constructor');

        this._ref = ref;
        this._blazorRef = blazorRef;

        fromEvent(document, 'click')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onClick());
    }

    public dispose() {
        debugLog?.log('dispose');

        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onClick = () => {
        if (!this._ref.classList.contains('open'))
            return;

        const container = this._ref.querySelector('.c-container');
        const withinMenu = event.composedPath().includes(container);
        if (withinMenu)
            return;

        this._blazorRef.invokeMethodAsync('Close');
        stopEvent(event);
    };
}
