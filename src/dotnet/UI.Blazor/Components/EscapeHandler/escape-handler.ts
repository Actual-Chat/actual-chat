import { fromEvent, Subject, takeUntil} from 'rxjs';

export interface Disposable {
    dispose(): void;
}

export class EscapeHandler implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    public static create(elementRef: HTMLDivElement, blazorRef: DotNet.DotNetObject): EscapeHandler {
        return new EscapeHandler(elementRef, blazorRef);
    }

    constructor(element: HTMLDivElement, blazorRef: DotNet.DotNetObject) {
        fromEvent<KeyboardEvent & { target: Element; }>(element, 'keydown')
            .pipe(takeUntil(this.disposed$))
            .subscribe(async event => {
                if (event.keyCode === 27 || event.key === 'Escape' || event.key === 'Esc') {
                    await blazorRef.invokeMethodAsync('OnEscape');
                }
            })
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }
}
