import { default as NoSleep } from 'nosleep.js';
import { addInteractionHandler } from 'first-interaction';

const LogScope = 'KeepAwakeUI';
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

addInteractionHandler('KeepAwakeUI', async () => {
    console.debug(`${LogScope}.onFirstInteraction: warming up noSleep`);
    await noSleep.enable();
    noSleep.disable();
    return true;
});
