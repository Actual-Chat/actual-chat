import { Observable } from 'rxjs';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'OnDeviceAwake';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class OnDeviceAwake {
    public static readonly events = new EventHandlerSet<void>(handlers => ensureWorker(handlers));
    public static readonly event$ = new Observable<void>(subject => {
        const handler = this.events.add(() => subject.next());
        return () => handler.dispose();
    })
}

let worker: Worker = null;

const onWakeUp = () => {
    debugLog?.log(`onWakeUp`);
    OnDeviceAwake.events.triggerSilently();
};

const onWorkerError = (error: ErrorEvent) => {
    errorLog?.log(`onWorkerError: unhandled error:`, error)
};

const createWorker = () => {
    const worker = new Worker('/dist/onDeviceAwakeWorker.js');
    worker.onmessage = onWakeUp;
    worker.onerror = onWorkerError;
    return worker;
}

const ensureWorker = (handlers: Set<EventHandler<void>>) => {
    const requiresWorker = handlers.size != 0;
    const hasWorker = worker != null;
    if (requiresWorker == hasWorker)
        return;

    if (requiresWorker) {
        debugLog?.log(`ensureWorker: creating worker`);
        worker = createWorker();
    } else {
        debugLog?.log(`ensureWorker: terminating worker`);
        worker.terminate();
        worker = null;
    }
};
