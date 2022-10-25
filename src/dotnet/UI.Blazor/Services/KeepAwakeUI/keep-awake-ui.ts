import { default as NoSleep } from 'nosleep.js';
import { NextInteraction } from 'next-interaction';
import { Log, LogLevel } from 'logging';

const LogScope = 'KeepAwakeUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const infoLog = Log.get(LogScope, LogLevel.Info);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const noSleep = new NoSleep();

export class KeepAwakeUI {
    public static async setKeepAwake(mustKeepAwake: boolean) {
        if (mustKeepAwake && !noSleep.isEnabled) {
            infoLog?.log(`setKeepAwake: enabling`);
            await noSleep.enable();
        } else if (!mustKeepAwake && noSleep.isEnabled) {
            infoLog?.log(`setKeepAwake: disabling`);
            noSleep.disable();
        }
    };
}

NextInteraction.addHandler(async () => {
    debugLog?.log(`warming up noSleep`);
    await noSleep.enable();
    noSleep.disable();
});
