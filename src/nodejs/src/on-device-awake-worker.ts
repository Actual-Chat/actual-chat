import { Log, LogLevel } from 'logging';
import 'logging-init';

const CHECK_INTERVAL = 1000;
const SLEEP_THRESHOLD = 20000;
const LogScope = 'on-device-awake-worker';
const debugLog = Log.get(LogScope, LogLevel.Debug);

let _lastTime: number = Date.now();

const checkForAwakeAfterSleep = () => {
    const currentTime =  Date.now();

    if ((currentTime - _lastTime) > SLEEP_THRESHOLD) {
        debugLog?.log(`checkForAwakeAfterSleep: woke up after sleep`);
        postMessage('wakeup');
    }

    _lastTime = currentTime;
};

setInterval(checkForAwakeAfterSleep, CHECK_INTERVAL);
