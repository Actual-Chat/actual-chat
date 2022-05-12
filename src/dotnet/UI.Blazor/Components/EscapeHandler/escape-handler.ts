import { Disposable } from './disposable';
import { Subject, takeUntil } from 'rxjs';
import escapist from './escapist';

export class EscapeHandler implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    public static create(blazorRef: DotNet.DotNetObject): EscapeHandler {
        return new EscapeHandler(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(async _ => {
                await blazorRef.invokeMethodAsync('OnEscape');
            });
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }
}
