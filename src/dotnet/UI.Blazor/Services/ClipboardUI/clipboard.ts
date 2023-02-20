import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'Clipboard';
const errorLog = Log.get(LogScope, LogLevel.Error);

export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}
