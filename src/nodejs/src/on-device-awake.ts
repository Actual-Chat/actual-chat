import { Log, LogLevel } from 'logging';

const LogScope = 'on-device-awake';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

let _worker: Worker = null;
const _handlers = new Array<() => void>();

const onWorkerMessage = () => {
    debugLog?.log(`onWorkerMessage`);
    _handlers.forEach(handler => {
        try {
            handler();
        } catch (error) {
            errorLog?.log(`onWorkerMessage: unhandled error in onDeviceAwake event handler:`, error)
        }
    });
};

const onWorkerError = (error: ErrorEvent) => {
    errorLog?.log(`onWorkerError: unhandled error:`, error)
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
