import { fromEvent, Subject, takeUntil, Subscription } from 'rxjs';
import { throttle } from 'promises';

export class ChatActivityPanel {
    private blazorRef: DotNet.DotNetObject;
    private readonly activityPanel: HTMLElement;
    private chatView: HTMLElement;
    private header: HTMLElement;
    private endAnchor: HTMLElement | null;
    private isMoving: boolean = false;
    private disposed$: Subject<void> = new Subject<void>();
    private pointerMoveSub: Subscription | null = null;
    private pointerUpSub: Subscription | null = null;

    static create(blazorRef: DotNet.DotNetObject, activityPanel: HTMLElement): ChatActivityPanel {
        return new ChatActivityPanel(blazorRef, activityPanel);
    }

    constructor(blazorRef: DotNet.DotNetObject, activityPanel: HTMLElement) {
        this.blazorRef = blazorRef;
        this.activityPanel = activityPanel;
        this.chatView = document.querySelector('.chat-view')!;
        this.header = this.activityPanel.closest('.layout-header')!;
        this.endAnchor = this.chatView ? this.chatView.querySelector('.c-end-anchor') : null;

        if (this.chatView && this.header && this.endAnchor) {
            fromEvent(this.chatView, 'scroll')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => {
                    this.throttledScrollHandler();
                });

            fromEvent<PointerEvent>(this.header, 'pointerdown')
                .pipe(takeUntil(this.disposed$))
                .subscribe(e => this.onPointerDown(e));
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.pointerMoveSub?.unsubscribe();
        this.pointerUpSub?.unsubscribe();
    }

    private onPointerDown(e: PointerEvent): void {
        this.pointerMoveSub = fromEvent<PointerEvent>(document, 'pointermove')
            .pipe(takeUntil(this.disposed$))
            .subscribe(ev => this.onPointerMove(ev));

        this.pointerUpSub = fromEvent<PointerEvent>(document, 'pointerup')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onPointerUp());
    }

    private onPointerMove(e: PointerEvent): void {
        if (e.movementY > 0)
            this.expandHeader();
    }

    private onPointerUp(): void {
        this.pointerMoveSub?.unsubscribe();
        this.pointerUpSub?.unsubscribe();
        this.pointerMoveSub = null;
        this.pointerUpSub = null;
    }

    private throttledScrollHandler = throttle(() => this.onScrollHandler(), 300);
    private onScrollHandler(): void {
        this.collapseHeader();
    }

    private collapseHeader(): void {
        if (this.isMoving)
            return;

        this.isMoving = true;
        this.allowMove(100);
        setTimeout(() => {
            this.header.classList.remove('expanded');
            if (!this.header.classList.contains('collapsed'))
                this.header.classList.add('collapsed');
        }, 1000);
    }
    private expandHeader(): void {
        if (this.isMoving)
            return;

        this.isMoving = true;
        this.allowMove(5000);

        this.header.classList.remove('collapsed');
        if (!this.header.classList.contains('expanded'))
            this.header.classList.add('expanded');
    }

    private allowMove(delay: number) {
        setTimeout(() => {
            this.isMoving = false;
        }, delay);
    }
}
