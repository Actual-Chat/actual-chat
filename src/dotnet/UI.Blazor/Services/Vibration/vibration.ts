import { delayAsync } from 'promises';
import { Log, LogLevel } from 'logging';

const LogScope = 'Vibration';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class Vibration {
    public static tunes = {
        beginRecording: [100,50,50],
        endRecording: [100],
    }

    public static vibrate(durationMs: number = 20): void {
        const canVibrate = ('vibrate' in navigator);
        if (!canVibrate)
            return;

        debugLog?.log('vibrate:', durationMs)
        window.navigator.vibrate(durationMs);
    }

    public static vibrateAndWait(durationMs: number): Promise<void> {
        this.vibrate(durationMs);
        return delayAsync(durationMs);
    }

    public static play(tuneName: string): void {
        void this.playAndWait(tuneName);
    }

    public static async playAndWait(tuneName: string): Promise<void> {
        const tune = this.tunes[tuneName];
        if (!tune) {
            warnLog?.log('play: no tune', tune);
            return;
        }

        for (let i = 0; i < tune.length; i++) {
            const durationMs = tune[i];
            if (i % 2 == 0)
                await this.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }
}
