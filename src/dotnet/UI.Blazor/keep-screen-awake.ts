import { default as NoSleep } from 'nosleep.js';
import { addInteractionHandler } from '../../nodejs/src/first-interaction';

const noSleep = new NoSleep();
const LogScope = 'keep-screen-awake';

const setNoSleepEnabled = async (enable: boolean) => {
    if (enable && !noSleep.isEnabled) {
        console.debug(`${LogScope}.setNoSleepEnabled: enabling noSleep`);
        await noSleep.enable();
    } else if (!enable && noSleep.isEnabled) {
        console.debug(`${LogScope}.setNoSleepEnabled: disabling noSleep`);
        noSleep.disable();
    }
};

addInteractionHandler('keep-screen-awake', async () => {
    console.debug(`${LogScope}.onFirstInteraction: warming noSleep up`);
    await noSleep.enable();
    noSleep.disable();
    return false;
});

export { setNoSleepEnabled };
