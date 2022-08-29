import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';

enum SideNavDirection {
    LeftToRight,
    RightToLeft,
}

interface SideNavOptions {
    direction: SideNavDirection;
    firstRender: boolean;
}

export class SideNav implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

    private touchStartX?: number = null;
    private touchStartY?: number = null;
    private translate?: number = null;
    private diffX?: number = null;
    private diffY?: number = null;

    public static create(
        element: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        options: SideNavOptions): SideNav {
        return new SideNav(element, blazorRef, options);
    }

    constructor(
        private readonly element: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options: SideNavOptions,
    ) {
        const position = window.getComputedStyle(element, null).position;
        if (position === 'static') {
            return;
        } else if (element.classList.contains('side-nav-right')) {
            element.classList.replace('side-nav-open', 'side-nav-closed');
        }

        fromEvent(this.element, 'transitionend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                if (element.classList.contains('side-nav-open')){
                    blazorRef.invokeMethodAsync('OnOpen');
                } else if (element.classList.contains('side-nav-closed')){
                    blazorRef.invokeMethodAsync('OnClosed');
                }
            });

        fromEvent(this.element, 'touchstart')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                element.classList.remove('side-nav-animate');
                this.touchStartX = e.touches[0].clientX;
                this.touchStartY = e.touches[0].clientY;
            });

        fromEvent(this.element, 'touchmove')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                if (this.touchStartX === null || this.touchStartY === null) {
                    element.classList.remove('side-nav-animate');
                    this.touchStartX = e.touches[0].clientX;
                    this.touchStartY = e.touches[0].clientY;
                }

                this.diffX = this.touchStartX - e.touches[0].clientX;
                this.diffY = this.touchStartY - e.touches[0].clientY;

                if (Math.abs(this.diffY) > Math.abs(this.diffX)) {
                    return;
                }

                if (this.options.direction == SideNavDirection.LeftToRight) {
                    if (this.diffX > 0) {
                        // closing <-
                        this.translate = element.clientWidth / 100 * this.diffX * -1 / 10;
                    } else {
                        // opening ->
                        this.translate = -100 + element.clientWidth / 100 * this.diffX * -1 / 10;
                    }

                    if (this.translate >= -100 && this.translate <= 0) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                } else {
                    if (this.diffX > 0) {
                        // opening <-
                        this.translate = 100 - element.clientWidth / 100 * this.diffX / 10;
                    } else {
                        // closing ->
                        this.translate = element.clientWidth / 100 * this.diffX * -1 / 10;
                    }

                    if (this.translate >= 0 && this.translate <= 100) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                }
            });

        fromEvent(this.element, 'touchend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                element.classList.add('side-nav-animate');
                element.style.transform = null;

                if (Math.abs(this.diffY) > Math.abs(this.diffX)) {
                    this.touchStartX = null;
                    this.touchStartY = null;
                    this.translate = null;
                    this.diffX = null;
                    this.diffY = null;

                    return;
                }

                element.classList.remove('side-nav-open');
                element.classList.remove('side-nav-closed');

                if (this.options.direction == SideNavDirection.RightToLeft) {
                    if (this.translate <= 50) {
                        element.classList.add('side-nav-open');
                    } else {
                        element.classList.add('side-nav-closed');
                    }
                } else {
                    if (this.translate <= -50) {
                        element.classList.add('side-nav-closed');
                    } else {
                        element.classList.add('side-nav-open');
                    }
                }

                this.touchStartX = null;
                this.touchStartY = null;
                this.translate = null;
                this.diffX = null;
                this.diffY = null;
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
