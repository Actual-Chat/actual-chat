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

    private touchStartX?: number = null;
    private touchStartY?: number = null;
    private touchStartAt?: number = null;
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
            // desktop view
            return;
        }

        fromEvent(this.element, 'transitionend')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                if (element.classList.contains('side-nav-open')){
                    blazorRef.invokeMethodAsync('OnOpened');
                } else if (element.classList.contains('side-nav-closed')){
                    blazorRef.invokeMethodAsync('OnClosed');
                }
            });

        fromEvent(this.element, 'touchstart', { passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                element.classList.remove('side-nav-animate');
                this.touchStartAt = Date.now();
                this.touchStartX = e.touches[0].clientX;
                this.touchStartY = e.touches[0].clientY;
            });

        fromEvent(this.element, 'touchmove', { passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe((e: TouchEvent) => {
                if (this.touchStartX === null || this.touchStartY === null) {
                    element.classList.remove('side-nav-animate');
                    this.touchStartAt = Date.now();
                    this.touchStartX = e.touches[0].clientX;
                    this.touchStartY = e.touches[0].clientY;
                }

                this.diffX = this.touchStartX - e.touches[0].clientX;
                this.diffY = this.touchStartY - e.touches[0].clientY;

                if (Math.abs(this.diffY) > Math.abs(this.diffX)) {
                    return;
                }

                const isOpened = element.classList.contains('side-nav-open');
                this.translate = 0;

                if (this.options.direction == SideNavDirection.LeftToRight) {
                    if (this.diffX > 0) {
                        if (isOpened) {
                            // closing <-
                            this.translate = -1 * this.diffX / element.clientWidth * 100;
                        }
                    } else {
                        if (!isOpened) {
                            // opening ->
                            this.translate = -100 + (-1 * this.diffX / element.clientWidth * 100);
                        }
                    }

                    if (this.translate >= -100 && this.translate <= 0) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                } else {
                    if (this.diffX > 0) {
                        if (!isOpened) {
                            // opening <-
                            this.translate = 100 - this.diffX / element.clientWidth * 100;
                        }
                    } else {
                        if (isOpened) {
                            // closing ->
                            this.translate = -1 * this.diffX / element.clientWidth * 100;
                        }
                    }

                    if (this.translate >= 0 && this.translate <= 100) {
                        element.style.transform = `translate3d(${this.translate}%, 0, 0)`;
                    }
                }
            });

        fromEvent(this.element, 'touchend', { passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                this.resetTransform(element);

                const isHorizontalSwipe = this.diffX && this.diffY && Math.abs(this.diffX) > Math.abs(this.diffY);

                if (!isHorizontalSwipe) {
                    this.resetTouch();
                    return;
                }

                const isOpened = element.classList.contains('side-nav-open');
                element.classList.remove('side-nav-open');
                element.classList.remove('side-nav-closed');

                const elapsed = Date.now() - this.touchStartAt;
                // Swipes with distance more than 50px in less than 300ms are considered as fast swipes.
                // Use fast swipes to toggle side nav panel.
                const isFastSwipe = elapsed < 300 && Math.abs(this.diffX) > 50;
                let isOpen = false;
                const swipeToLeft = this.diffX > 0;
                if (this.options.direction == SideNavDirection.RightToLeft) {
                    if (isFastSwipe) {
                        if (isOpened && !swipeToLeft)
                            isOpen = false;
                        else if (!isOpened && swipeToLeft)
                            isOpen = true;
                        else
                            isOpen = isOpened;
                    }
                    else
                        isOpen = this.translate <= 50;
                } else {
                    if (isFastSwipe) {
                       if (isOpened && swipeToLeft)
                           isOpen = false;
                       else if (!isOpened && !swipeToLeft)
                           isOpen = true;
                       else
                           isOpen = isOpened;
                    }
                    else
                        isOpen = !(this.translate <= -50);
                }
                element.classList.add(isOpen ? 'side-nav-open' : 'side-nav-closed');

                this.resetTouch();
            });

        fromEvent(this.element, 'touchcancel', { passive: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                this.resetTransform(element);
                this.resetTouch();
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private resetTransform(element: HTMLDivElement)
    {
        element.classList.add('side-nav-animate');
        element.style.transform = null;
    }

    private resetTouch()
    {
        this.touchStartAt = null;
        this.touchStartX = null;
        this.touchStartY = null;
        this.translate = null;
        this.diffX = null;
        this.diffY = null;
    }
}
