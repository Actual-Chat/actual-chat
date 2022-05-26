import {addInteractionHandler} from 'first-interaction';
import {default as NoSleep} from 'nosleep.js';

const LogScope = 'NoSleep';

const noSleep = new NoSleep();

const keepDisplayAwake = () => {
    addInteractionHandler(LogScope, () => noSleep.enable().then());
}

export {keepDisplayAwake};
