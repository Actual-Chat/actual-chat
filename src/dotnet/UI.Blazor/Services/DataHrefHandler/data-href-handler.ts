import { fromEvent } from 'rxjs';
import { endEvent } from 'event-handling';
import { Log, LogLevel } from 'logging';

import { FocusUI } from '../FocusUI/focus-ui';

const LogScope = 'DataHrefHandler';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class DataHrefHandler {
    public static init(): void {
        fromEvent(document, 'click')
            .subscribe((event: Event) => this.tryNavigateOnDataHref(event));
    }

    private static tryNavigateOnDataHref(event: Event): void {
        const isElement = event.target instanceof Element;
        if (!isElement)
            return;

        const closestElement = event.target.closest('[data-href]');
        if (!(closestElement instanceof HTMLElement))
            return;

        endEvent(event);
        const url = closestElement.dataset['href'];
        debugLog?.log(`tryNavigateOnDataHref: navigating to`, url);
        FocusUI.blur();
        (window as any).Blazor.navigateTo(url);
    }
}

DataHrefHandler.init();
