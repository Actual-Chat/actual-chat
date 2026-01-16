import { fromEvent, merge, Subject } from 'rxjs';
import { filter, first, switchMap, takeUntil, tap, throttleTime } from 'rxjs/operators';

export class ChatActivityPanel {
    private readonly activityPanel: HTMLElement;
    private chatView: HTMLElement;
    private header: HTMLElement;
    private disposed$: Subject<void> = new Subject<void>();

    private state: 'expanded' | 'collapsed' = 'expanded';
    private lockUntil = 0;
    private isMoving = false;

    static create(activityPanel: HTMLElement): ChatActivityPanel {
        return new ChatActivityPanel(activityPanel);
    }

    constructor(activityPanel: HTMLElement) {
        this.activityPanel = activityPanel;
        this.chatView = document.querySelector('.chat-view')!;
        this.header = this.activityPanel.closest('.layout-header')!;

        if (!this.chatView || !this.header) {
            return;
        }

        this.lockUntil = Date.now() + 3000;
        fromEvent(this.chatView, 'scroll').pipe(
            throttleTime(200),
            filter(() => {
                if (this.isMoving)
                    this.isMoving = false;

                return true;
            }),
            filter(() => !this.isLocked()),
            takeUntil(this.disposed$)
        ).subscribe(() => this.collapse());

        merge(
            fromEvent<PointerEvent>(this.header, 'pointerdown'),
            fromEvent<PointerEvent>(this.activityPanel, 'pointerdown'),
            fromEvent<PointerEvent>(this.chatView, 'pointerdown')
        ).pipe(
            tap(startEvent => {
                this.isMoving = true;
                startEvent.preventDefault();
            }),
            switchMap(startEvent => {
                const startY = startEvent.clientY;
                const startX = startEvent.clientX;

                return fromEvent<PointerEvent>(document, 'pointermove').pipe(
                    filter(moveEvent => {
                        const dy = moveEvent.clientY - startY;
                        const dx = Math.abs(moveEvent.clientX - startX);
                        return dy > 6 && dy > dx;
                    }),
                    first(),
                    takeUntil(
                        fromEvent(document, 'pointerup').pipe(
                            tap(() => {
                                this.isMoving = false;
                            })
                        )
                    )
                );
            }),
            takeUntil(this.disposed$)
        ).subscribe(() => this.manualExpand());
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.header.classList.remove('expanded', 'collapsed');
        this.disposed$.next();
        this.disposed$.complete();
    }

    private manualExpand() {
        this.isMoving = false;
        if (this.state === 'expanded')
            return;

        this.expand();
        this.lockUntil = Date.now() + 5000;
    }

    private isLocked(): boolean {
        const now = Date.now();
        return now < this.lockUntil;
    }

    private collapse() {
        if (this.state === 'collapsed')
            return;

        this.state = 'collapsed';
        this.header.classList.remove('expanded');
        this.header.classList.add('collapsed');
    }

    private expand() {
        if (this.state === 'expanded')
            return;

        this.state = 'expanded';
        this.header.classList.remove('collapsed');
        this.header.classList.add('expanded');
    }
}
