import { default as NoSleep } from 'nosleep.js';
import { NextInteraction } from 'next-interaction';

const LogScope = 'KeepAwakeUI';
const debug = true;
const noSleep = new NoSleep();

export class KeepAwakeUI {
    public static async setKeepAwake(mustKeepAwake: boolean) {
        if (mustKeepAwake && !noSleep.isEnabled) {
            console.debug(`${LogScope}.setKeepAwake: enabling`);
            await noSleep.enable();
        } else if (!mustKeepAwake && noSleep.isEnabled) {
            console.debug(`${LogScope}.setKeepAwake: disabling`);
            noSleep.disable();
        }
    };
}

NextInteraction.addHandler(async () => {
    if (debug)
        console.debug(`${LogScope}: warming up noSleep`);
    await noSleep.enable();
    noSleep.disable();
});
