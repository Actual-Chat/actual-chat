import { fromEvent, Subject, takeUntil } from 'rxjs';
import { debounce, PromiseSourceWithTimeout, throttle } from 'promises';

export class DateVisor {
    private readonly dateVisor: HTMLElement;
    private chatView: HTMLElement;
    private subHeader: HTMLElement;
    private isScrolling: boolean;
    private disposed$: Subject<void> = new Subject<void>();

    static create(dateVisor: HTMLElement): DateVisor {
        return new DateVisor(dateVisor);
    }

    constructor(dateVisor: HTMLElement) {
        this.dateVisor = dateVisor;
        const checkInterval = setInterval(() => {
            this.chatView = document.querySelector('.chat-view');
            this.subHeader = this.dateVisor.closest('.layout-subheader');
            if (this.chatView && this.subHeader) {
                clearInterval(checkInterval);

                fromEvent(this.chatView, 'scroll')
                    .pipe(takeUntil(this.disposed$))
                    .subscribe(this.onScrollHandler);
            }
        }, 200);
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
        scrollWithTimeout.setTimeout(250, () => {
            this.onScrollThrottled();
        });
    }

    private onScrollThrottled = throttle(() => this.onScroll(), 250, 'delayHead');
    private onScroll() {
        if (this.isScrolling) {
            this.getDateVisorTransform();
        }
        if (!this.dateVisor.classList.contains('show')) {
            this.dateVisor.classList.add('show');
        }
    }

    private onScrollStopDebounced = debounce(() => this.onScrollStop(), 1000);
    private onScrollStop() {
        this.isScrolling = false;
        this.dateVisor.classList.remove('show');
    }

    private getDateVisorTransform() {
        let conversationHeaders = this.chatView.querySelectorAll('.conversation-header');
        let parentItems = Array.from(conversationHeaders)
            .map(header => header.closest('.item'))
            .filter(Boolean);

        let subHeaderBottom = this.subHeader.getBoundingClientRect().bottom;

        let stuckItems = parentItems.filter(item => {
            let itemTop = item.getBoundingClientRect().top;
            return Math.abs(itemTop - subHeaderBottom) < 1;
        });

        if (stuckItems.length > 0) {
            let tallestItem = stuckItems.reduce((tallest: HTMLElement, current: HTMLElement) => {
                return current.offsetHeight > tallest.offsetHeight ? current : tallest;
            }, stuckItems[0]);
            let header = tallestItem.querySelector('.conversation-header') as HTMLElement;
            let height = header.offsetHeight;
            this.dateVisor.style.transform = `translateY(${height + 5}px)`;
        } else {
            this.dateVisor.style.transform = '';
        }
    }
}
