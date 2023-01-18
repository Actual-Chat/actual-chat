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
    private static _mustKeepAwake: boolean;

    public static async setKeepAwake(mustKeepAwake: boolean) {
        this._mustKeepAwake = mustKeepAwake;
        if (mustKeepAwake && !noSleep.isEnabled) {
            infoLog?.log(`setKeepAwake: enabling`);
            try {
                await noSleep.enable();
            }
            catch (e) {
                errorLog?.log(`setKeepAwake(true): error:`, e);
            }
        } else if (!mustKeepAwake && noSleep.isEnabled) {
            infoLog?.log(`setKeepAwake: disabling`);
            try {
                noSleep.disable();
            }
            catch (e) {
                errorLog?.log(`setKeepAwake(false): error:`, e);
            }
        }
    };

    public static async warmup() {
        debugLog?.log(`warmup`);
        if (!noSleep.isEnabled) {
            await noSleep.enable();
        }
        if (!this._mustKeepAwake) {
            noSleep.disable();
        }
    }
}

NextInteraction.addHandler(KeepAwakeUI.warmup);
