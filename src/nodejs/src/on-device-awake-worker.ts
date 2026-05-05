const CheckIntervalMs = 1000;
const MinDetectedSleepMs = 1500; // 1.5s

let _lastTime: number = Date.now();

const wakeUpCheck = () => {
    const currentTime =  Date.now();
    const sleepDurationMs = Math.max(0, currentTime - _lastTime - CheckIntervalMs);
    _lastTime = currentTime;
    if (sleepDurationMs > MinDetectedSleepMs)
        postMessage(sleepDurationMs);
};

setInterval(wakeUpCheck, CheckIntervalMs);
onmessage = () => wakeUpCheck();

export {};
