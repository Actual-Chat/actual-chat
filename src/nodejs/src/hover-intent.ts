import { Subject, takeUntil } from 'rxjs';
import { Disposable } from 'disposable';
import { DocumentEvents } from 'event-handling';

export interface HoverIntentOptions {
    delayMs: number;
    isEnabled: () => boolean;
    resolveKey: (target: Element | null) => readonly [Element, string] | null;
    onSettled: (key: string, element: Element) => void;
    onLeave: () => void;
}

export class HoverIntent implements Disposable {
    private readonly disposed$ = new Subject<void>();
    private pendingKey: string | null = null;
    private activeKey: string | null = null;
    private timer: ReturnType<typeof setTimeout> | null = null;
    private lastX = 0;
    private lastY = 0;

    constructor(private readonly options: HoverIntentOptions) {
        DocumentEvents.passive.pointerOver$
            .pipe(takeUntil(this.disposed$))
            .subscribe(event => this.onPointerOver(event));
    }

    public dispose(): void {
        this.clearTimer();
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onPointerOver(event: PointerEvent): void {
        this.lastX = event.clientX;
        this.lastY = event.clientY;
        if (!this.options.isEnabled()) {
            this.reset();
            return;
        }

        const hit = this.options.resolveKey(event.target as Element | null);
        if (!hit) {
            this.reset();
            return;
        }

        const key = hit[1];
        if (key === this.activeKey) {
            // Cursor is still over the shown item (incl. its menu) — keep it, cancel any pending switch
            this.clearTimer();
            return;
        }

        // Cursor moved to a different item — hide the shown menu right away, then arm the dwell
        // timer so the new item opens only after the delay (no switch on a fast sweep).
        this.hideActive();
        if (key === this.pendingKey)
            return;

        this.clearTimer();
        this.pendingKey = key;
        this.timer = setTimeout(() => this.onTimer(key), this.options.delayMs);
    }

    private onTimer(key: string): void {
        this.timer = null;
        this.pendingKey = null;
        // Re-resolve from current geometry: catches both a moved pointer and content scrolled
        // under a stationary pointer (which fires no pointerover), so no menu pops on a stale target.
        const element = document.elementFromPoint(this.lastX, this.lastY);
        const hit = this.options.resolveKey(element);
        if (hit?.[1] !== key)
            return;

        this.activeKey = key;
        this.options.onSettled(key, hit[0]);
    }

    private reset(): void {
        this.clearTimer();
        this.hideActive();
    }

    private hideActive(): void {
        if (this.activeKey == null)
            return;

        this.activeKey = null;
        this.options.onLeave();
    }

    private clearTimer(): void {
        if (this.timer != null) {
            clearTimeout(this.timer);
            this.timer = null;
        }
        this.pendingKey = null;
    }
}
