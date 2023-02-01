import { debounceTime, filter, fromEvent, map, merge, Subject, takeUntil } from 'rxjs';
import { stopEvent } from 'event-handling';
import { ScreenSize } from '../../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log, LogLevel, LogScope } from 'logging';
const LogScope: LogScope = 'Docs';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class DocsHeader {
    private readonly disposed$ = new Subject<void>();
    private readonly menu: HTMLElement;

    static create(docs: HTMLElement, blazorRef: DotNet.DotNetObject): DocsHeader {
        return new DocsHeader(docs, blazorRef);
    }

    constructor(
        private readonly docs: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        if (ScreenSize.isNarrow()) {
            this.menu = docs.querySelector('.landing-menu');
            fromEvent(document, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onClick())
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    // Event handlers

    private onClick() {
        if (!this.menu.classList.contains('open'))
            return;

        const container = this.menu.querySelector('.c-container');
        const withinMenu = event.composedPath().includes(container);
        if (withinMenu)
            return;

        this.blazorRef.invokeMethodAsync('CloseMenu');
        stopEvent(event);
    };
}
