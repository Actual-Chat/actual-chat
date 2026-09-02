import { Disposable } from 'disposable';
import { fromEvent, Subject, take, takeUntil } from 'rxjs';

const DragThresholdPx = 4;

// Makes the collapsed incoming-call island draggable. A tap on the avatar/body (no real move) still
// bubbles to Blazor's expand handler; once a drag actually moves the island, the trailing click is
// swallowed so it doesn't also re-open the modal. Taps on the Accept/Decline buttons are left alone.
export class CollapsedIncomingCall implements Disposable {
    private readonly disposed$ = new Subject<void>();
    private dragging = false;
    private moved = false;
    private startX = 0;
    private startY = 0;
    private originX = 0;
    private originY = 0;
    private tx = 0;
    private ty = 0;

    public static create(root: HTMLElement): CollapsedIncomingCall {
        return new CollapsedIncomingCall(root);
    }

    constructor(private readonly root: HTMLElement) {
        fromEvent<PointerEvent>(root, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.onDown(e));
        fromEvent<MouseEvent>(root, 'click', { capture: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => {
                if (!this.moved)
                    return;
                e.stopPropagation();
                e.preventDefault();
                this.moved = false;
            });
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onDown(e: PointerEvent): void {
        const target = e.target as HTMLElement | null;
        if (target?.closest('button, [role="button"]'))
            return; // Accept / Decline keep acting.

        this.dragging = true;
        this.moved = false;
        this.startX = e.clientX;
        this.startY = e.clientY;
        this.originX = this.tx;
        this.originY = this.ty;
        this.root.setPointerCapture(e.pointerId);

        const up$ = fromEvent<PointerEvent>(this.root, 'pointerup').pipe(take(1), takeUntil(this.disposed$));
        fromEvent<PointerEvent>(this.root, 'pointermove')
            .pipe(takeUntil(up$), takeUntil(this.disposed$))
            .subscribe(ev => this.onMove(ev));
        up$.subscribe(() => this.dragging = false);
    }

    private onMove(e: PointerEvent): void {
        if (!this.dragging)
            return;

        const dx = e.clientX - this.startX;
        const dy = e.clientY - this.startY;
        if (!this.moved && Math.hypot(dx, dy) < DragThresholdPx)
            return;

        this.moved = true;
        this.tx = this.originX + dx;
        this.ty = this.originY + dy;
        this.root.style.transform = `translate(${this.tx}px, ${this.ty}px)`;
    }
}
