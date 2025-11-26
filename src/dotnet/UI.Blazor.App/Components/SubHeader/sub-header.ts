import { fromEvent, Subject, takeUntil } from 'rxjs';

export class SubHeader {
    private readonly disposed$: Subject<void> = new Subject<void>();

    static create(subHeaderLit: HTMLElement, blazorRef: DotNet.DotNetObject): SubHeader {
        return new SubHeader(subHeaderLit, blazorRef);
    }

    constructor(
        private readonly subHeaderLit: HTMLElement | null,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.subHeaderLit = subHeaderLit;
        if (this.subHeaderLit == null)
            return;

        if (!(this.subHeaderLit instanceof HTMLElement))
            return;

        fromEvent(this.subHeaderLit, 'animationend')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: AnimationEvent) => {
                if (e.animationName === "showSubHeader") {
                    this.setHeight();
                }
                this.blazorRef.invokeMethodAsync('OnAnimationEnd');
            });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();

    }

    public setHeight() {
        if (this.subHeaderLit == null)
            return;

        const h = this.subHeaderLit.offsetHeight;
        this.subHeaderLit.style.setProperty('--subheader-height', h + 'px');
    }
}
