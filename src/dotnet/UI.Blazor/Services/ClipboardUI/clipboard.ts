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
