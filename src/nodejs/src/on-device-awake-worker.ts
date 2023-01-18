import { Log, LogLevel } from 'logging';

const CheckIntervalMs = 1000;
const SleepThresholdMs = 20000;
const LogScope = 'on-device-awake-worker';
const debugLog = Log.get(LogScope, LogLevel.Debug);

let _lastTime: number = Date.now();

const checkForAwakeAfterSleep = () => {
    const currentTime =  Date.now();

    if ((currentTime - _lastTime) > SleepThresholdMs) {
        debugLog?.log(`checkForAwakeAfterSleep: woke up after sleep`);
        postMessage('wakeup');
    }
    _lastTime = currentTime;
};

setInterval(checkForAwakeAfterSleep, CheckIntervalMs);
