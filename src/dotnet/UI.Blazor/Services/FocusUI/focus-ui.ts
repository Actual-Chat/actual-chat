import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'FocusUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);

export class FocusUI {
    public static focus(targetRef: HTMLElement): void {
        debugLog?.log(`focus, target:`, targetRef)
        targetRef.focus();
    }

    public static blur(): void {
        debugLog?.log(`blur()`);
        const activeElement = document.activeElement as HTMLElement;
        if (activeElement != null && activeElement.blur != null)
            activeElement.blur();
    }
}
