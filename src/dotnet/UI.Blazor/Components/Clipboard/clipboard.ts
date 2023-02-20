import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'Clipboard';
const errorLog = Log.get(LogScope, LogLevel.Error);

export function selectAndCopy(inputRef: HTMLInputElement, text: string | null) {
    inputRef.select();
    const blob = new Blob([text ?? inputRef.value], { type: 'text/plain' });
    const clipboardItem = new ClipboardItem({ ['text/plain']: blob });
    return navigator.clipboard.write([clipboardItem]).catch(e => errorLog?.log('selectAndCopy: failed to write to clipboard', e));
}

export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}
