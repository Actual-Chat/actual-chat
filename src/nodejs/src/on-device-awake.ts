import { Observable } from 'rxjs';
import { EventHandlerSet } from 'event-handling';
import { Log } from 'logging';
import { Versioning } from 'versioning';

const { debugLog, errorLog } = Log.get('OnDeviceAwake');

export class OnDeviceAwake {
    private static _totalSleepDurationMs = 0;
    private static _worker: Worker = null;

    public static get totalSleepDurationMs(): number { return this._totalSleepDurationMs; }
    public static readonly events = new EventHandlerSet<number>();
    public static readonly event$ = new Observable<number>(subject => {
        const handler = this.events.add(x => subject.next(x));
        return () => handler.dispose();
    })

    public static init(): void {
        debugLog?.log(`init`);
        const onSleepDetected = (event: MessageEvent<number>) => {
            this._totalSleepDurationMs = event.data;
            debugLog?.log(`onSleepDetected: total sleep duration:`, this._totalSleepDurationMs / 1000, 'seconds');
            OnDeviceAwake.events.triggerSilently(event.data);
        };

        const onWorkerError = (error: ErrorEvent) => {
            errorLog?.log(`onWorkerError: unhandled error:`, error)
        };

        const workerPath = Versioning.mapPath('/dist/onDeviceAwakeWorker.js');
        this._worker = new Worker(workerPath);
        this._worker.onmessage = onSleepDetected;
        this._worker.onerror = onWorkerError;
    }
}

OnDeviceAwake.init();
