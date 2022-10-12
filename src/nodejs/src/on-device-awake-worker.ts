const CHECK_INTERVAL = 1000;
const SLEEP_THRESHOLD = 20000;
const LogScope = 'on-device-awake-worker';

let _lastTime: number = Date.now();

const checkForAwakeAfterSleep = () => {
    const currentTime =  Date.now();

    if ((currentTime - _lastTime) > SLEEP_THRESHOLD) {
        console.debug(`${LogScope}.checkForAwakeAfterSleep`, 'woke up after sleep');
        postMessage('wakeup');
    }

    _lastTime = currentTime;
};

setInterval(checkForAwakeAfterSleep, CHECK_INTERVAL);
