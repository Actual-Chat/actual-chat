import { fromEvent } from 'rxjs';
import { Log, LogLevel } from 'logging';
import { FocusUI } from '../FocusUI/focus-ui';

const LogScope = 'Navigator';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class Navigator {
    constructor() {
        fromEvent(document, 'click')
            .subscribe(event => {
                this.tryNavigate(event);
            });
    }

    private tryNavigate(event: Event): void  {
        const isElement = event.target instanceof Element;
        if (!isElement)
            return undefined;
        const closestElement = event.target.closest('[data-href]');
        const isHtmlElement = closestElement instanceof HTMLElement;
        if (!isHtmlElement)
            return undefined;
        const url = closestElement.dataset['href'];
        debugLog?.log(`navigation: url:`, url);
        FocusUI.blur();
        (<any>window).Blazor.navigateTo(url);
    }
}

const navigator = new Navigator();

export default navigator;
