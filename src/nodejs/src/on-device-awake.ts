import { Observable } from 'rxjs';
import { DocumentEvents, EventHandlerSet } from 'event-handling';
import { getLogs } from 'logging';
import { Versioning } from 'versioning';

const { debugLog, infoLog, errorLog } = getLogs('OnDeviceAwake');

export class OnDeviceAwake {
    private static _totalSleepDurationMs = 0;
    private static _worker: Worker | null = null;

    public static get totalSleepDurationMs(): number { return this._totalSleepDurationMs; }
    public static readonly events = new EventHandlerSet<number>();
    public static readonly event$ = new Observable<number>(subject => {
        const handler = this.events.add(x => subject.next(x));
        return () => handler.dispose();
    })

    public static init(): void {
        debugLog?.log(`init`);
        const onSleepDetected = (event: MessageEvent<number>) => {
            this._totalSleepDurationMs += event.data;
            infoLog?.log(`onSleepDetected: +${event.data / 1000}s, total: ${this._totalSleepDurationMs / 1000}s`);
            OnDeviceAwake.events.triggerSilently(this._totalSleepDurationMs);
        };

        const onWorkerError = (error: ErrorEvent) => {
            errorLog?.log(`onWorkerError: unhandled error:`, error)
        };

        const workerPath = Versioning.mapPath('/dist/onDeviceAwakeWorker.js');
        this._worker = new Worker(workerPath, { type: 'module' });
        this._worker.onmessage = onSleepDetected;
        this._worker.onerror = onWorkerError;

        DocumentEvents.passive.visibilityChange$.subscribe(() => {
            if (!document.hidden)
                this._worker?.postMessage(null);
        });
    }

    public static fakeSleep(durationMs: number): void {
        this._totalSleepDurationMs += durationMs;
        infoLog?.log(`fakeSleep: +${durationMs / 1000}s, total: ${this._totalSleepDurationMs / 1000}s`);
        this.events.triggerSilently(this._totalSleepDurationMs);
    }
}

OnDeviceAwake.init();
