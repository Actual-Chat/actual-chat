// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-floating-promises, @typescript-eslint/restrict-plus-operands */
import { fromEvent, Subject, takeUntil } from 'rxjs';

export class SubHeader {
    private readonly disposed$: Subject<void> = new Subject<void>();

    static create(subHeaderLit: HTMLElement, blazorRef: DotNet.DotNetObject): SubHeader {
        return new SubHeader(subHeaderLit, blazorRef);
    }

    constructor(
        private readonly subHeaderLit: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject
    ) {
        this.subHeaderLit = subHeaderLit;
        if (!(this.subHeaderLit instanceof HTMLElement))
            return;

        fromEvent(this.subHeaderLit, 'animationend')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: AnimationEvent) => {
                if (e.animationName === 'showSubHeader') {
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
        if (this.subHeaderLit.classList.contains('audio-subheader'))
            return;

        const h = this.subHeaderLit.offsetHeight;
        this.subHeaderLit.style.setProperty('--subheader-height', h + 'px');
    }
}
