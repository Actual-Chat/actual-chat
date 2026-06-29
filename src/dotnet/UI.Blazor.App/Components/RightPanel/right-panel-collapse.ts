import { fromEvent, Subject } from 'rxjs';
import { filter, takeUntil } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

const COLLAPSE_RANGE_REM = 7.5;  // list scroll distance mapped to a full collapse (header tracks scroll 1:1)
const EXPAND_AT_PX = 8;          // within this of the list top => pinned fully expanded (exact 0)
// Require this much scroll overflow before collapsing — comfortably above the freed range so the
// list still overflows once collapsed (otherwise collapsing makes it fit, scrollTop snaps to 0,
// and the header oscillates against the list every frame).
const MIN_OVERFLOW_REM = 10;
const DRAG_START_PX = 6;
const SNAP_DURATION_MS = 250;
const ACTIVE_LINGER_MS = 150;    // keep dependent transitions suppressed this long after the last scroll write
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

function clamp01(v: number): number {
    return v < 0 ? 0 : v > 1 ? 1 : v;
}

// Collapse of the right-panel header, driven by the active tab's scroll position.
// --rp-collapse = (offset - anchor) / range, so scrolling the list down collapses the header
// proportionally (1:1, smoothly tracked). It is a one-way ratchet: scrolling up mid-list holds
// the state instead of expanding — the header only re-opens by reaching the list top (an
// animated snap, not scroll-tracked) or by a header drag. The one-way scroll response is
// deliberate: the header collapse resizes the list viewport, and a two-way (symmetric) response
// would feed that relayout back into an oscillation. Touch-only: disabled on wide screens.
export class RightPanelCollapse {
    private readonly rightPanel: HTMLElement;
    private readonly content: HTMLElement | null;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly rangePx = remToPx(COLLAPSE_RANGE_REM);
    private readonly minOverflowPx = remToPx(MIN_OVERFLOW_REM);

    private progress = 0;       // current value: 0 = expanded, 1 = collapsed (mirrored to --rp-collapse)
    private anchorOffset = 0;   // list offset at which the header is fully expanded
    private lastScroller: HTMLElement | null = null;
    private snapRaf = 0;
    private lingerTimer = 0;
    private headerClassObserver: MutationObserver | null = null;
    private wasAvatarExpanded = false;

    static create(rightPanel: HTMLElement): RightPanelCollapse {
        return new RightPanelCollapse(rightPanel);
    }

    constructor(rightPanel: HTMLElement) {
        this.rightPanel = rightPanel;
        this.content = rightPanel.querySelector('.c-panel-content');
        if (!this.content)
            return;

        // Capture phase catches the active tab's inner scroller (VirtualList or plain
        // overflow) without touching its scrolling.
        fromEvent(this.content, 'scroll', { capture: true }).pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => this.onScroll(e));

        // Drag the header itself to expand/collapse, following the finger (touch only).
        DocumentEvents.capturedPassive.touchStart$.pipe(
            filter(() => !ScreenSize.isWide() && !this.isDragging && !this.isSnapping && !this.isAvatarExpanded),
            filter(e => this.header?.contains(e.target as Node) ?? false),
            takeUntil(this.disposed$),
        ).subscribe(e => Gestures.addActive(new CollapseDragGesture(this, e)));

        // Watch the full-screen-avatar class so closing it re-baselines us on the next scroll.
        const header = this.header;
        if (header) {
            this.wasAvatarExpanded = header.classList.contains('expanded-header');
            this.headerClassObserver = new MutationObserver(() => this.onHeaderClassChange());
            this.headerClassObserver.observe(header, { attributes: true, attributeFilter: ['class'] });
        }
    }

    public get header(): HTMLElement | null {
        return this.rightPanel.querySelector('.c-header');
    }

    public get isAvatarExpanded(): boolean {
        return this.header?.classList.contains('expanded-header') ?? false;
    }

    public get isDragging(): boolean {
        return this.rightPanel.classList.contains('dragging');
    }

    public get isSnapping(): boolean {
        return this.snapRaf !== 0;
    }

    public get dragRange(): number {
        return this.rangePx;
    }

    public getProgress(): number {
        return this.progress;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        cancelAnimationFrame(this.snapRaf);
        clearTimeout(this.lingerTimer);
        this.headerClassObserver?.disconnect();
        this.rightPanel.classList.remove('snapping', 'dragging', 'collapsing', 'collapsed');
        this.rightPanel.style.removeProperty('--rp-collapse');
        this.disposed$.next();
        this.disposed$.complete();
    }

    private onScroll(e: Event) {
        // Ignore scroll while a drag/snap (and the layout shift it triggers) plays out.
        if (this.isDragging || this.isSnapping || this.isAvatarExpanded)
            return;

        const scroller = e.target;
        if (!(scroller instanceof HTMLElement))
            return;
        if (ScreenSize.isWide()) {
            this.setProgress(0); // desktop: never collapse
            return;
        }

        // Fresh scroller (tab switch / after full-screen close): baseline at the current
        // position so the header starts expanded and never collapses on its own.
        if (scroller !== this.lastScroller) {
            this.lastScroller = scroller;
            this.anchorOffset = this.topOffset(scroller) ?? 0;
            this.markActive();
            this.setProgress(0);
            return;
        }

        const offset = this.topOffset(scroller);
        if (offset === null)
            return; // list top not in the rendered window — keep the current state

        // Reaching the top (or a list too short to collapse) is the only scroll-driven way to
        // expand, and it ANIMATES rather than tracks the scroll: tracking the expand would grow
        // the header mid-scroll, resize the list viewport, and feed back into a collapse/expand
        // jitter loop. A freshly settled list also lands a few px from the top, so the EXPAND_AT_PX
        // band doubles as the sub-pixel-stable "fully expanded" zone.
        if (offset <= EXPAND_AT_PX || !this.canCollapse(scroller)) {
            this.anchorOffset = 0;
            if (this.progress !== 0)
                this.snapTo(0);
            return;
        }

        // Ratchet: scrolling down collapses, proportionally and smoothly tracking the list.
        // Scrolling up mid-list does NOT expand here — it holds the current state (re-pegging
        // the anchor) so the header only re-opens at the top or via a header drag. This one-way
        // scroll response is what keeps the layout-feedback loop from oscillating: the viewport
        // growth a collapse causes can only drop the measured offset, which the ratchet ignores
        // instead of bouncing the header back open.
        const raw = clamp01((offset - this.anchorOffset) / this.rangePx);
        if (raw >= this.progress)
            this.setProgress(raw);
        else
            this.anchorOffset = offset - this.progress * this.rangePx;
    }

    // Drag lifecycle (driven by CollapseDragGesture)

    public beginDrag() {
        cancelAnimationFrame(this.snapRaf);
        this.snapRaf = 0;
        clearTimeout(this.lingerTimer);
        this.rightPanel.classList.remove('snapping', 'collapsing');
        this.rightPanel.classList.add('dragging');
    }

    public setDragProgress(progress: number) {
        this.setProgress(progress);
    }

    public endDrag(terminalProgress: number) {
        const target = terminalProgress > 0.5 ? 1 : 0;
        this.rightPanel.classList.remove('dragging');
        // Re-anchor to the current list position so the swipe sticks: a later scroll continues
        // from this state instead of snapping back to the position-derived value.
        const offset = this.lastScroller ? this.topOffset(this.lastScroller) : null;
        if (offset !== null)
            this.anchorOffset = Math.max(0, offset - target * this.rangePx);
        this.snapTo(target);
    }

    private onHeaderClassChange() {
        const isExpanded = this.isAvatarExpanded;
        if (this.wasAvatarExpanded && !isExpanded)
            this.lastScroller = null; // full-screen avatar closed => re-baseline on next scroll
        this.wasAvatarExpanded = isExpanded;
    }

    private setProgress(progress: number) {
        if (progress === this.progress)
            return;

        this.progress = progress;
        this.rightPanel.style.setProperty('--rp-collapse', progress.toFixed(4));
        this.rightPanel.classList.toggle('collapsed', progress > 0.5);
        this.markActive();
    }

    // Suppress the dependent properties' own CSS transitions while the scroll drives the var,
    // so the header tracks the list instantly; restore them shortly after scrolling stops
    // (then the expanded/collapsed-header avatar feature animates as before).
    private markActive() {
        if (this.isDragging || this.isSnapping)
            return;

        this.rightPanel.classList.add('collapsing');
        clearTimeout(this.lingerTimer);
        this.lingerTimer = window.setTimeout(() => this.rightPanel.classList.remove('collapsing'), ACTIVE_LINGER_MS);
    }

    // Frame-driven snap to 0/1 on drag release. `snapping` kills the dependent properties'
    // own transitions so the avatar and header move in lockstep, and makes onScroll bail so
    // the layout shift the collapse causes can't restart it.
    private snapTo(target: number) {
        cancelAnimationFrame(this.snapRaf);
        this.rightPanel.classList.remove('collapsing');
        this.rightPanel.classList.add('snapping');
        const start = this.progress;
        const startedAt = Date.now();
        const step = () => {
            const t = Math.min(1, (Date.now() - startedAt) / SNAP_DURATION_MS);
            this.setProgress(start + (target - start) * easeOutCubic(t));
            if (t < 1) {
                this.snapRaf = requestAnimationFrame(step);
            }
            else {
                this.snapRaf = 0;
                this.setProgress(target);
                this.rightPanel.classList.remove('snapping');
            }
        };
        this.snapRaf = requestAnimationFrame(step);
    }

    // Distance of the current scroll position from the list's logical top (0 at the top),
    // or null when the top is not in the rendered window (scrolled deep into the list).
    private topOffset(scroller: HTMLElement): number | null {
        const scrollController = (scroller as unknown as ScrollLimitsSource).scrollController;
        if (!scrollController)
            return Math.max(0, scroller.scrollTop);

        const { min, max } = scrollController.getEffectiveScrollLimits();
        if (!Number.isFinite(min))
            return null;
        const banded = Math.max(min, Number.isFinite(max) ? Math.min(scroller.scrollTop, max) : scroller.scrollTop);
        return banded - min;
    }

    // Don't collapse a list that can't scroll enough to fill the space the collapse frees.
    // The overflow is measured as if the header were fully expanded (the space the current
    // collapse already freed is added back), so the decision is invariant to the current
    // progress and can't flip as the header collapses; requiring a margin above the freed
    // range guarantees the list still overflows once collapsed, which breaks the layout
    // feedback loop (collapse -> list fits -> scrollTop clamped to 0 -> expand -> repeat).
    private canCollapse(scroller: HTMLElement): boolean {
        const overflow = this.scrollOverflow(scroller);
        if (overflow === null)
            return true; // unbounded (infinite) list — always plenty to scroll
        return overflow + this.progress * this.rangePx > this.minOverflowPx;
    }

    private scrollOverflow(scroller: HTMLElement): number | null {
        const scrollController = (scroller as unknown as ScrollLimitsSource).scrollController;
        if (!scrollController)
            return scroller.scrollHeight - scroller.clientHeight;

        const { min, max } = scrollController.getEffectiveScrollLimits();
        if (!Number.isFinite(min) || !Number.isFinite(max))
            return null;
        return max - min;
    }
}

// Gesture: drag the header vertically to collapse (up) / expand (down), following the
// finger, then snap to 0/1 on release based on terminal velocity.
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

        const range = panel.dragRange;
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
                    this.panel.beginDrag();
                }

                tryPreventDefaultForEvent(e);
                // Drag down (dy > 0) expands => progress decreases; drag up collapses.
                this.progress = Math.max(0, Math.min(1, startProgress - dy / range));

                const now = Date.now();
                const dt = now - this.lastTime;
                if (dt > 0) {
                    this.velocity = (this.progress - this.lastProgress) / dt * 1000;
                    this.lastProgress = this.progress;
                    this.lastTime = now;
                }
                this.panel.setDragProgress(this.progress);
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
