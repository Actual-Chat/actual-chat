import { Disposable } from 'disposable';
import { Subject, takeUntil } from 'rxjs';
import Escapist from './escapist';

export class EscapistSubscription implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    public static create(blazorRef: DotNet.DotNetObject): EscapistSubscription {
        return new EscapistSubscription(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        Escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => blazorRef.invokeMethodAsync('OnEscape'));
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
