import { fromEvent, Subject, takeUntil } from 'rxjs';

export class DateVisor {
    private readonly dateVisor: HTMLElement;
    private readonly chatView: HTMLElement;
    private timer: NodeJS.Timer;
    private disposed$: Subject<void> = new Subject<void>();

    static create(dateVisor: HTMLElement): DateVisor {
        return new DateVisor(dateVisor);
    }

    constructor(dateVisor: HTMLElement) {
        this.dateVisor = dateVisor;
        this.chatView = document.body.querySelector('.chat-view');

        if (this.chatView == null)
            return;

        this.timer = null;
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
        if (this.timer !== null) {
            if (!this.dateVisor.classList.contains('show') )
                this.dateVisor.classList.add('show');
            clearTimeout(this.timer);
        }
        this.timer = setTimeout(() => {
            this.dateVisor.classList.remove('show');
        }, 1000);
    }
}

