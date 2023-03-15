const CheckIntervalMs = 1000;
const MinDetectedSleepMs = 1500; // 1.5s

let _lastTime: number = Date.now();
let totalSleepDurationMs = 0;

const wakeUpCheck = () => {
    const currentTime =  Date.now();
    const sleepDurationMs = Math.max(0, currentTime - _lastTime - CheckIntervalMs);
    if (sleepDurationMs > MinDetectedSleepMs) {
        totalSleepDurationMs += sleepDurationMs;
        postMessage(totalSleepDurationMs);
    }
    _lastTime = currentTime;
};

setInterval(wakeUpCheck, CheckIntervalMs);
