import { fromEvent, Subject } from 'rxjs';
import { filter, takeUntil } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

const COLLAPSE_RANGE_REM = 7.5;
const COLLAPSE_SCROLL_THRESHOLD_PX = 24;
const DRAG_START_PX = 6;
const SNAP_DURATION_MS = 250;
const Deceleration = 0.1;

interface ScrollLimitsSource {
    scrollController?: { getEffectiveScrollLimits(): { min: number; max: number } };
}

function remToPx(rem: number): number {
    return rem * (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16);
}

function easeOutCubic(t: number): number {
    return 1 - Math.pow(1 - t, 3);
}

export class RightPanelCollapse {
    private readonly rightPanel: HTMLElement;
    private readonly content: HTMLElement | null;
    private readonly disposed$: Subject<void> = new Subject<void>();

    // Cached once: reading getComputedStyle() per scroll event would force a style flush.
    private readonly rangePx = remToPx(COLLAPSE_RANGE_REM);

    private progress = 0;
    private snapRaf = 0;
    private collapsingEndTimer = 0;
    private lastScroller: HTMLElement | null = null;
    private scrollBaseline = 0;

    static create(rightPanel: HTMLElement): RightPanelCollapse {
        return new RightPanelCollapse(rightPanel);
    }

    constructor(rightPanel: HTMLElement) {
        this.rightPanel = rightPanel;
        this.content = rightPanel.querySelector('.c-panel-content');
        if (!this.content)
            return;

        // Scroll-linked collapse: the header progress follows the active tab's
        // scroll position frame-by-frame. Capture phase catches the inner scroller
        // (VirtualList or plain overflow) without touching its scrolling.
        fromEvent(this.content, 'scroll', { capture: true }).pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => this.onScroll(e));

        // Drag the header itself to collapse/expand, following the finger (touch only).
        DocumentEvents.capturedPassive.touchStart$.pipe(
            filter(() => !ScreenSize.isWide() && !this.isDragging),
            filter(e => this.header?.contains(e.target as Node) ?? false),
            takeUntil(this.disposed$),
        ).subscribe(e => Gestures.addActive(new CollapseDragGesture(this, e)));
    }

    public get header(): HTMLElement | null {
        return this.rightPanel.querySelector('.c-header');
    }

    public get isDragging(): boolean {
        return this.rightPanel.classList.contains('dragging');
    }

    public getProgress(): number {
        return this.progress;
    }

    public get range(): number {
        return this.rangePx;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        cancelAnimationFrame(this.snapRaf);
        clearTimeout(this.collapsingEndTimer);
        this.rightPanel.classList.remove('dragging', 'collapsing', 'collapsed');
        this.rightPanel.style.removeProperty('--rp-collapse');
        this.disposed$.next();
        this.disposed$.complete();
    }

    // Drag lifecycle (driven by CollapseDragGesture)

    public beginDrag() {
        cancelAnimationFrame(this.snapRaf);
        this.snapRaf = 0;
        // Drag is finger-synced and frame-by-frame, so drop the scroll-smoothing
        // marker — otherwise the variable's transition would lag behind the finger.
        clearTimeout(this.collapsingEndTimer);
        this.rightPanel.classList.remove('collapsing');
        this.rightPanel.classList.add('dragging');
    }

    public setProgress(progress: number) {
        this.progress = progress;
        this.rightPanel.style.setProperty('--rp-collapse', progress.toFixed(4));
        this.rightPanel.classList.toggle('collapsed', progress > 0.5);
    }

    public endDrag(terminalProgress: number) {
        const target = terminalProgress > 0.5 ? 1 : 0;
        // Re-anchor the scroll baseline so a later scroll continues from the snapped
        // state instead of fighting it.
        if (this.lastScroller)
            this.scrollBaseline = this.lastScroller.scrollTop - target * this.rangePx;
        this.animateTo(target, () => this.rightPanel.classList.remove('dragging'));
    }

    private onScroll(e: Event) {
        if (this.isDragging)
            return;

        const scroller = e.target as HTMLElement;
        if (!(scroller instanceof HTMLElement))
            return;
        if (ScreenSize.isWide()) {
            this.setProgress(0);
            return;
        }

        cancelAnimationFrame(this.snapRaf);
        this.snapRaf = 0;

        // A different scroller means a tab switch: treat its current position as the
        // expanded "top" baseline, so switching tabs never collapses on its own.
        if (scroller !== this.lastScroller) {
            this.lastScroller = scroller;
            this.scrollBaseline = Math.max(0, scroller.scrollTop);
            this.setProgress(0);
            return;
        }

        const p = this.computeProgress(scroller);
        if (p === null)
            return; // Top edge not known (scrolled away from it) — leave the state as is.

        this.markCollapsing();
        this.setProgress(p);
    }

    // Progress is measured against the list's own logical top, never a hand-tracked
    // baseline. In a VirtualList that top (and its overscroll band) drifts as items
    // load/unload and the container re-anchors, so we read ScrollController's live
    // limits and clamp into the band — which makes top/bottom overscroll a no-op
    // (no collapse flash) and survives re-anchoring without a stale baseline.
    private computeProgress(scroller: HTMLElement): number | null {
        const range = this.rangePx;
        const scrollController = (scroller as unknown as ScrollLimitsSource).scrollController;
        if (scrollController) {
            const { min, max } = scrollController.getEffectiveScrollLimits();
            if (!Number.isFinite(min))
                return null; // First item not discovered => we're below the top region.

            const scrollable = Number.isFinite(max) ? max - min : Number.POSITIVE_INFINITY;
            // Don't collapse a list that barely overflows: it would leave an empty gap.
            if (scrollable <= range + COLLAPSE_SCROLL_THRESHOLD_PX)
                return 0;

            const banded = Math.max(min, Number.isFinite(max) ? Math.min(scroller.scrollTop, max) : scroller.scrollTop);
            return Math.max(0, Math.min(1, (banded - min) / range));
        }

        // Native-scroll tabs (e.g. Members) have no ScrollController: the floored
        // baseline keeps an iOS rubber-band (negative scrollTop) from collapsing.
        this.scrollBaseline = Math.max(0, Math.min(this.scrollBaseline, scroller.scrollTop));
        const canCollapse = scroller.scrollHeight > scroller.clientHeight + range + COLLAPSE_SCROLL_THRESHOLD_PX;
        return canCollapse ? Math.max(0, Math.min(1, (scroller.scrollTop - this.scrollBaseline) / range)) : 0;
    }

    // Mark an active scroll-collapse session so CSS smooths the driver variable
    // (and disables per-property transitions). Cleared shortly after scrolling
    // settles, restoring the transitions used by the expanded-header feature.
    private markCollapsing() {
        this.rightPanel.classList.add('collapsing');
        clearTimeout(this.collapsingEndTimer);
        this.collapsingEndTimer = self.setTimeout(() => {
            this.rightPanel.classList.remove('collapsing');
        }, 160);
    }

    private animateTo(target: number, onDone?: () => void) {
        cancelAnimationFrame(this.snapRaf);
        const start = this.progress;
        const startedAt = Date.now();
        const step = () => {
            const t = Math.min(1, (Date.now() - startedAt) / SNAP_DURATION_MS);
            this.setProgress(start + (target - start) * easeOutCubic(t));
            if (t < 1) {
                this.snapRaf = requestAnimationFrame(step);
            } else {
                this.snapRaf = 0;
                onDone?.();
            }
        };
        this.snapRaf = requestAnimationFrame(step);
    }
}

// Gesture: drag the header vertically to collapse (up) / expand (down),
// following the finger, then snap on release based on terminal velocity.
class CollapseDragGesture extends Gesture {
    private started = false;
    private progress: number;
    private velocity = 0;
    private lastProgress: number;
    private lastTime: number;

    constructor(private panel: RightPanelCollapse, touchStartEvent: TouchEvent) {
        super();
        const origin = getCoords(touchStartEvent);
        if (!origin) {
            this.dispose();
            return;
        }

        const range = panel.range;
        const startProgress = panel.getProgress();
        this.progress = startProgress;
        this.lastProgress = startProgress;
        this.lastTime = Date.now();

        this.addDisposables(
            DocumentEvents.capturedPassive.touchCancel$.subscribe(() => this.finish()),
            DocumentEvents.capturedPassive.touchEnd$.subscribe(() => this.finish()),
            DocumentEvents.active.touchMove$.subscribe(e => {
                const coords = getCoords(e);
                if (!coords)
                    return;

                const dy = coords.y - origin.y;
                if (!this.started) {
                    if (Math.abs(dy) < DRAG_START_PX)
                        return;
                    this.started = true;
                    panel.beginDrag();
                }

                tryPreventDefaultForEvent(e);
                // Drag down (dy > 0) expands → progress decreases; drag up collapses.
                this.progress = Math.max(0, Math.min(1, startProgress - dy / range));

                const now = Date.now();
                const dt = now - this.lastTime;
                if (dt > 0) {
                    this.velocity = (this.progress - this.lastProgress) / dt * 1000;
                    this.lastProgress = this.progress;
                    this.lastTime = now;
                }
                panel.setProgress(this.progress);
            }),
        );
    }

    private finish() {
        if (this.started) {
            const decelerationDistance = this.velocity * Math.abs(this.velocity) / (2 * Deceleration);
            const terminal = Math.max(0, Math.min(1, this.progress + decelerationDistance));
            this.panel.endDrag(terminal);
        }
        this.dispose();
    }
}

function getCoords(event?: TouchEvent): { x: number; y: number } | null {
    const touches = event?.changedTouches ?? event?.touches;
    if (!touches?.length)
        return null;

    const touch = touches[0];
    return { x: touch.pageX, y: touch.pageY };
}
