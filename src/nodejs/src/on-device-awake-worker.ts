import { Log, LogLevel, LogScope } from 'logging';

const CheckIntervalMs = 1000;
const SleepThresholdMs = 4800; // 5s
const LogScope: LogScope = 'OnDeviceAwakeWorker';
const debugLog = Log.get(LogScope, LogLevel.Debug);

let _lastTime: number = Date.now();

const wakeUpCheck = () => {
    const currentTime =  Date.now();
    if ((currentTime - _lastTime) > SleepThresholdMs) {
        debugLog?.log(`wakeUpCheck: woke up after sleep`);
        postMessage('wakeup');
    }
    _lastTime = currentTime;
};

setInterval(wakeUpCheck, CheckIntervalMs);
