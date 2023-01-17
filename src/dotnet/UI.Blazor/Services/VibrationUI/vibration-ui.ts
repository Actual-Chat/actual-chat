import { delayAsync } from 'promises';
import { Log, LogLevel } from 'logging';
import { InteractiveUI } from '../InteractiveUI/interactive-ui';

const LogScope = 'VibrationUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class VibrationUI {
    public static vibrate(durationMs: number = 20): void {
        const canVibrate = ('vibrate' in navigator);
        if (!canVibrate) {
            debugLog?.log(`vibrate(${durationMs}ms): unsupported by browser`)
            return;
        }

        if (!InteractiveUI.isInteractive) {
            debugLog?.log(`vibrate(${durationMs}ms): suppressed by InteractiveUI`)
            return;
        }

        if (!window.navigator.vibrate(durationMs)) {
            warnLog?.log(`vibrate(${durationMs}ms): suppressed by browser`)
            return;
        }

        debugLog?.log(`vibrate(${durationMs}ms)`)
    }

    public static vibrateAndWait(durationMs: number): Promise<void> {
        this.vibrate(durationMs);
        return delayAsync(durationMs);
    }
}
