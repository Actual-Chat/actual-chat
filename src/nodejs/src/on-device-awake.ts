const LogScope = 'on-device-awake';

let _worker: Worker = null;
const _handlers = new Array<() => void>();

const onWorkerMessage = () => {
    console.debug(LogScope, 'onWorkerMessage', 'received');
    _handlers.forEach(handler => {
        try {
            handler();
        } catch (err) {
            console.error(LogScope, 'onWorkerMessage', 'onDeviceAwake event handler failed with error')
        }
    });
};

const onWorkerError = (error: ErrorEvent) => {
    console.error(`FileName: ${error.filename} LineNumber: ${error.lineno} Message: ${error.message}`);
};

const createWorker = () => {
    const worker = new Worker('/dist/onAwakeWorker.js');
    worker.onmessage = onWorkerMessage;
    worker.onerror = onWorkerError;

    return worker;
}

const ensureWorker = () => {
    if (_handlers.length > 0 && _worker === null) {
        console.debug(`${LogScope}.ensureWorker`, 'creating worker')
        _worker = createWorker();
    }
    if (_handlers.length === 0 && _worker !== null) {
        console.debug(`${LogScope}.ensureWorker`, 'terminating worker')
        _worker.terminate();
        _worker = null;
    }
};

const onDeviceAwake = (handler: () => void): () => void => {
    console.debug(`${LogScope}.onAwake`, 'adding onAwakeHandler', handler)
    _handlers.push(handler);
    ensureWorker();
    return () => {
        const index = _handlers.indexOf(handler);
        if (index >= 0) {
            _handlers.splice(index, 1);
            ensureWorker();
        }
    }
}

export {onDeviceAwake};
