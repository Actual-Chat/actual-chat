import { Log, LogLevel } from 'logging';

const LogScope = 'FocusUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class FocusUI {
    public static focus(targetRef: HTMLElement): void {
        debugLog?.log(`focus, target:`, targetRef)
        targetRef.focus();
    }

    public static blur(): void {
        debugLog?.log(`blur()`)
        const activeElement = document.activeElement as HTMLElement;
        if (activeElement != null && activeElement.blur != null)
            activeElement.blur();
    }
}
