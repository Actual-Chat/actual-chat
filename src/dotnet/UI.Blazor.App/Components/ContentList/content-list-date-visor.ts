import { fromEvent, Subject, takeUntil } from 'rxjs';
import { debounce, throttle } from 'actuallab-core';

export class ContentListDateVisor {
    private readonly wrapper: HTMLElement;
    private readonly badge: HTMLElement;
    private scroller: HTMLElement | null = null;
    private checkInterval: ReturnType<typeof setInterval> | null = null;
    private isScrolling = false;
    private isDisposed = false;
    private disposed$: Subject<void> = new Subject<void>();

    static create(wrapper: HTMLElement): ContentListDateVisor {
        return new ContentListDateVisor(wrapper);
    }

    constructor(wrapper: HTMLElement) {
        this.wrapper = wrapper;
        this.badge = wrapper.querySelector<HTMLElement>('.date-visor')!;
        const checkInterval = setInterval(() => {
            this.scroller = wrapper.parentElement?.querySelector<HTMLElement>('.content-list-tab') ?? null;
            if (this.scroller) {
                clearInterval(checkInterval);
                this.checkInterval = null;
                fromEvent(this.scroller, 'scroll')
                    .pipe(takeUntil(this.disposed$))
                    .subscribe(this.onScroll);
            }
        }, 300);
        this.checkInterval = checkInterval;
    }

    public dispose() {
        if (this.checkInterval !== null) {
            clearInterval(this.checkInterval);
            this.checkInterval = null;
        }
        if (this.isDisposed)
            return;
        this.isDisposed = true;
        this.disposed$.next();
        this.disposed$.complete();
    }

    private onScroll = () => {
        this.isScrolling = true;
        this.onScrollStopDebounced();
        this.onScrollThrottled();
    };

    private onScrollThrottled = throttle(() => this.update(), 100);

    private update() {
        // Resting at the very top shows the newest group header inline, so the visor stays hidden there.
        const scroller = this.scroller;
        if (!this.isScrolling || !scroller || scroller.scrollTop <= 4) {
            this.hide();
            return;
        }
        const date = this.topVisibleDate(scroller);
        if (date === null) {
            this.hide();
            return;
        }
        this.badge.textContent = date;
        this.wrapper.classList.add('show');
    }

    private onScrollStopDebounced = debounce(() => this.onScrollStop(), 800);
    private onScrollStop() {
        this.isScrolling = false;
        this.hide();
    }

    private hide() {
        this.wrapper.classList.remove('show');
    }

    private topVisibleDate(scroller: HTMLElement): string | null {
        const top = scroller.getBoundingClientRect().top;
        const rows = scroller.querySelectorAll<HTMLElement>('[data-visor-date]');
        for (const row of rows) {
            if (row.getBoundingClientRect().bottom > top + 1)
                return row.dataset.visorDate ?? null;
        }
        return null;
    }
}
