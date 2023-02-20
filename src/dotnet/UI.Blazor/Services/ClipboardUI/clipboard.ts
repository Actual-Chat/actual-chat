import { Log, LogLevel, LogScope } from 'logging';
import { DocumentEvents } from 'event-handling';
import { getOrInheritData } from 'dom-helpers';
import { exhaustMap, filter, map, tap, catchError } from 'rxjs';

const LogScope: LogScope = 'Clipboard';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const errorLog = Log.get(LogScope, LogLevel.Error);

export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}

// we intentionally do it on client side since iOS safari requires event handler stack for clipboard write access
function subscribeOnCopy() {
    DocumentEvents.active.click$.pipe().subscribe();
    debugLog?.log(`subscribeOnCopy`);
    DocumentEvents.active.click$
        .pipe(
            map(ev => {
                const [triggerElement, text] = getOrInheritData(ev.target, 'clipboardText');
                return text;
            }),
            filter(text => !!text),
            tap(() => debugLog?.log(`subscribeOnCopy: writing to clipboard`)),
            exhaustMap(text => navigator.clipboard.writeText(text)),
            catchError((err, caught) => {
                errorLog?.log(`subscribeOnCopy: failed to copy: `, err);
                return caught;
            }),
        ).subscribe();
}

subscribeOnCopy();
