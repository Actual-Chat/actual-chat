import { fromEvent, Subject, takeUntil } from 'rxjs';
import { debounce, PromiseSourceWithTimeout, throttle } from 'promises';

export class DateVisor {
    private readonly dateVisor: HTMLElement;
    private readonly chatView: HTMLElement;
    private isScrolling: boolean;
    private disposed$: Subject<void> = new Subject<void>();

    static create(dateVisor: HTMLElement): DateVisor {
        return new DateVisor(dateVisor);
    }

    constructor(dateVisor: HTMLElement) {
        this.dateVisor = dateVisor;
        this.chatView = document.body.querySelector('.chat-view');

        if (this.chatView == null)
            return;

        fromEvent(this.chatView, 'scroll')
            .pipe(takeUntil(this.disposed$))
            .subscribe(this.onScrollHandler);
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

    private onScrollThrottled = throttle(() => this.onScroll(), 300, 'delayHead');
    private onScroll() {
        if (this.isScrolling && !this.dateVisor.classList.contains('show') )
            this.dateVisor.classList.add('show');
    }

    private onScrollStopDebounced = debounce(() => this.onScrollStop(), 800);
    private onScrollStop() {
        this.isScrolling = false;
        this.dateVisor.classList.remove('show');
    }
}

