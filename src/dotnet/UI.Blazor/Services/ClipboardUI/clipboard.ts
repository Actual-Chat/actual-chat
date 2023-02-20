import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'Clipboard';
const errorLog = Log.get(LogScope, LogLevel.Error);
export async function selectAndCopy(inputRef: HTMLInputElement | string, text: string | null) {
    let input: HTMLInputElement = null;
    if (inputRef instanceof HTMLInputElement) {
        input = inputRef;
    } else {
        input = document.getElementById(inputRef) as HTMLInputElement;
        if (!(input instanceof HTMLInputElement)) {
            errorLog.log(`selectAndCopy: failed to find HTMLInputElement`)
            return;
        }
    }
    input.select();
    const blob = new Blob([text ?? input.value], { type: 'text/plain' });
    const clipboardItem = new ClipboardItem({ ['text/plain']: blob });
    return navigator.clipboard.write([clipboardItem]).catch(e => errorLog?.log('selectAndCopy: failed to write to clipboard', e));
}

export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}
