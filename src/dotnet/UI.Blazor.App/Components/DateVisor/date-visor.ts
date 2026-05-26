// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/no-deprecated */
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { debounce, PromiseSourceWithTimeout, throttle } from 'actuallab-core';

export class DateVisor {
    private readonly dateVisor: HTMLElement;
    private chatView: HTMLElement;
    private subHeader: HTMLElement;
    private endAnchor: HTMLElement | null;
    private isScrolling: boolean;
    private disposed$: Subject<void> = new Subject<void>();

    static create(dateVisor: HTMLElement): DateVisor {
        return new DateVisor(dateVisor);
    }

    constructor(dateVisor: HTMLElement) {
        this.dateVisor = dateVisor;
        const checkInterval = setInterval(() => {
            this.chatView = document.querySelector('.chat-view')!;
            this.subHeader = this.dateVisor.closest('.layout-subheader')!;
            this.endAnchor = this.chatView ? this.chatView.querySelector('.c-end-anchor') : null;
            if (this.chatView && this.subHeader && this.endAnchor) {
                clearInterval(checkInterval);

                fromEvent(this.chatView, 'scroll')
                    .pipe(takeUntil(this.disposed$))
                    .subscribe(this.onScrollHandler);
            }
        }, 800);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onScrollHandler = () => {
        this.isScrolling = true;
        this.onScrollStopDebounced();
        const scrollWithTimeout = new PromiseSourceWithTimeout<void>();
        scrollWithTimeout.setTimeout(800, () => {
            this.onScrollThrottled();
        });
    }

    private onScrollThrottled = throttle(() => this.onScroll(), 250, 'delayHead');
    private onScroll() {
        if (this.isScrolling
            && !this.dateVisor.classList.contains('show')
            && !this.isInViewport(this.endAnchor, this.chatView)) {
            this.dateVisor.classList.add('show');
        }
    }

    private onScrollStopDebounced = debounce(() => this.onScrollStop(), 800);
    private onScrollStop() {
        this.isScrolling = false;
        this.dateVisor.classList.remove('show');
    }

    private isInViewport(anchor: HTMLElement | null, chatView: HTMLElement | null = null) {
        if (!anchor)
            return false;

        const rect = anchor.getBoundingClientRect();

        if (chatView) {
            const chatRect = chatView.getBoundingClientRect();
            return rect.top >= chatRect.top && rect.top < chatRect.bottom;
        }
        return false;
    }
}
