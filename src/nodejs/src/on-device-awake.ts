import { Log, LogLevel } from 'logging';

const LogScope = 'on-device-awake';
const debugLog = Log.get(LogScope, LogLevel.Debug);

let _worker: Worker = null;
const _handlers = new Array<() => void>();

const onWorkerMessage = () => {
    console.debug(LogScope, 'onWorkerMessage', 'received');
    _handlers.forEach(handler => {
        try {
            handler();
        } catch (err) {
            console.error(LogScope, 'onWorkerMessage', 'onDeviceAwake event handler failed with an error')
        }
    });
};

const onWorkerError = (error: ErrorEvent) => {
    console.error(`FileName: ${error.filename} LineNumber: ${error.lineno} Message: ${error.message}`);
};

const createWorker = () => {
    const worker = new Worker('/dist/onDeviceAwakeWorker.js');
    worker.onmessage = onWorkerMessage;
    worker.onerror = onWorkerError;

    return worker;
}

const ensureWorker = () => {
    if (_handlers.length > 0 && _worker === null) {
        debugLog?.log(`ensureWorker: creating worker`)
        _worker = createWorker();
    }
    if (_handlers.length === 0 && _worker !== null) {
        debugLog?.log(`ensureWorker: terminating worker`)
        _worker.terminate();
        _worker = null;
    }
};

const onDeviceAwake = (handler: () => void): () => void => {
    debugLog?.log(`onDeviceAwake: adding handler:`, handler)
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
