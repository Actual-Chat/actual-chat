import { Disposable } from 'disposable';
import { fromEvent, Subject, take, takeUntil } from 'rxjs';

const DragThresholdPx = 4;
const MarginPx = 8;

const clamp = (v: number, min: number, max: number): number => Math.max(min, Math.min(max, v));

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
    // Translate bounds that keep the island inside the viewport, captured at drag start.
    private minTx = 0;
    private maxTx = 0;
    private minTy = 0;
    private maxTy = 0;

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
        // A resize / rotation can strand the island off-screen; pull it back into view.
        fromEvent(window, 'resize')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.clampToViewport());
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

        // Own the gesture so a drag near a screen edge doesn't also swipe a side panel open.
        e.stopPropagation();
        this.dragging = true;
        this.moved = false;
        this.startX = e.clientX;
        this.startY = e.clientY;
        this.originX = this.tx;
        this.originY = this.ty;
        this.computeBounds();
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

        e.stopPropagation();
        this.moved = true;
        this.tx = clamp(this.originX + dx, this.minTx, this.maxTx);
        this.ty = clamp(this.originY + dy, this.minTy, this.maxTy);
        this.applyTransform();
    }

    // Translate range that keeps the island's rect within the viewport (minus a small margin). The
    // layout position is the current rect minus the applied translate.
    private computeBounds(): void {
        const rect = this.root.getBoundingClientRect();
        const layoutLeft = rect.left - this.tx;
        const layoutTop = rect.top - this.ty;
        this.minTx = MarginPx - layoutLeft;
        this.maxTx = window.innerWidth - rect.width - MarginPx - layoutLeft;
        this.minTy = MarginPx - layoutTop;
        this.maxTy = window.innerHeight - rect.height - MarginPx - layoutTop;
    }

    private clampToViewport(): void {
        this.computeBounds();
        this.tx = clamp(this.tx, this.minTx, this.maxTx);
        this.ty = clamp(this.ty, this.minTy, this.maxTy);
        this.applyTransform();
    }

    private applyTransform(): void {
        this.root.style.transform = `translate(${this.tx}px, ${this.ty}px)`;
    }
}
