import { Observable } from 'rxjs';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { Log } from 'logging';
import { Versioning } from 'versioning';

const { debugLog, errorLog } = Log.get('OnDeviceAwake');

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
    const workerPath = Versioning.mapPath('/dist/onDeviceAwakeWorker.js');
    const worker = new Worker(workerPath);
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
