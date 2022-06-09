import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';

enum SideNavDirection {
    LeftToRight,
    RightToLeft,
}

interface SideNavOptions {
    direction: SideNavDirection;
}

export class SideNav implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

    private touchStart?: number = null;
    private translate?: number;

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
        fromEvent(this.element, 'touchstart')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                e.preventDefault();
                element.classList.remove('side-nav-animate');
                this.touchStart = e.touches[0].clientX;
            });

        fromEvent(this.element, 'touchmove')
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                e.preventDefault();
                if (this.touchStart === null) {
                    element.classList.remove('side-nav-animate');
                    this.touchStart = e.touches[0].clientX;
                }

                const diff = this.touchStart - e.touches[0].clientX;

                if (this.options.direction == SideNavDirection.LeftToRight) {
                    if (diff > 0) {
                        // closing <-
                        this.translate = element.clientWidth / 100 * diff * -1 / 10;
                    } else {
                        // opening ->
                        this.translate = -100 + element.clientWidth / 100 * diff * -1 / 10;
                    }

                    if (this.translate >= -100 && this.translate <= 0) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                } else {
                    if (diff > 0) {
                        // opening <-
                        this.translate = 100 - element.clientWidth / 100 * diff / 10;
                    } else {
                        // closing ->
                        this.translate = element.clientWidth / 100 * diff * -1 / 10;
                    }

                    if (this.translate >= 0 && this.translate <= 100) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                }
            });

        fromEvent(this.element, 'touchend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(()  => {
                if (!this.touchStart)
                    return;

                element.classList.add('side-nav-animate');
                element.style.transform = null;

                if (this.options.direction == SideNavDirection.RightToLeft) {
                    if (this.translate <= 50) {
                        element.classList.add('side-nav-opened');
                        blazorRef.invokeMethodAsync('OnOpened');
                    } else {
                        element.classList.add('side-nav-closed');
                        blazorRef.invokeMethodAsync('OnClosed');
                    }
                } else {
                    if (this.translate <= -50) {
                        element.classList.add('side-nav-closed');
                        blazorRef.invokeMethodAsync('OnClosed');
                    } else {
                        element.classList.add('side-nav-opened');
                        blazorRef.invokeMethodAsync('OnOpened');
                    }
                }

                this.touchStart = null;
            });
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }
}
