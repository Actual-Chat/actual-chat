import { fromEvent, Subject } from 'rxjs';
import { filter, takeUntil } from 'rxjs/operators';
import { Gesture, Gestures } from 'gestures';
import { DocumentEvents, tryPreventDefaultForEvent } from 'event-handling';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

const EXPAND_AT_PX = 4;          // at/above the list top (offset <= this) => always expanded
const DIR_TRIGGER_PX = 24;       // directional scroll distance that flips the collapse state
const MIN_OVERFLOW_REM = 7.5;    // don't collapse a list too short to fill the freed header space
const DRAG_RANGE_REM = 7.5;      // finger travel that maps to a full collapse/expand
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

// Binary collapse of the right-panel header: it is only ever expanded (--rp-collapse 0)
// or collapsed (--rp-collapse 1), with a frame-driven snap between them. Scrolling the
// active tab past a threshold collapses; scrolling back to the top expands; dragging the
// header follows the finger and snaps on release. Touch-only (skipped on wide screens).
export class RightPanelCollapse {
    private readonly rightPanel: HTMLElement;
    private readonly content: HTMLElement | null;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly minOverflowPx = remToPx(MIN_OVERFLOW_REM);
    private readonly dragRangePx = remToPx(DRAG_RANGE_REM);

    private progress = 0;       // current animated value: 0 = expanded, 1 = collapsed
    private collapsed = false;  // logical target state
    private snapRaf = 0;
    private lastScroller: HTMLElement | null = null;
    private lastOffset: number | null = null;  // last seen top-offset (null = top out of window)
    private scrollAccum = 0;                    // directional scroll accumulator (px)
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

        // Drag the header itself to collapse/expand, following the finger (touch only).
        DocumentEvents.capturedPassive.touchStart$.pipe(
            filter(() => !ScreenSize.isWide() && !this.isDragging && !this.isSnapping && !this.isAvatarExpanded),
            filter(e => this.header?.contains(e.target as Node) ?? false),
            takeUntil(this.disposed$),
        ).subscribe(e => Gestures.addActive(new CollapseDragGesture(this, e)));

        // Watch the full-screen-avatar class so closing it (by tap) re-baselines us: the
        // next scroll starts from expanded, then collapses smoothly.
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
        return this.dragRangePx;
    }

    public getProgress(): number {
        return this.progress;
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        cancelAnimationFrame(this.snapRaf);
        this.headerClassObserver?.disconnect();
        this.rightPanel.classList.remove('snapping', 'dragging', 'collapsed');
        this.rightPanel.style.removeProperty('--rp-collapse');
        this.disposed$.next();
        this.disposed$.complete();
    }

    // Drag lifecycle (driven by CollapseDragGesture)

    public beginDrag() {
        cancelAnimationFrame(this.snapRaf);
        this.snapRaf = 0;
        this.rightPanel.classList.remove('snapping');
        this.rightPanel.classList.add('dragging');
    }

    public setProgress(progress: number) {
        this.progress = progress;
        this.rightPanel.style.setProperty('--rp-collapse', progress.toFixed(4));
        this.rightPanel.classList.toggle('collapsed', progress > 0.5);
    }

    public endDrag(terminalProgress: number) {
        const collapsed = terminalProgress > 0.5;
        this.collapsed = collapsed;
        this.rightPanel.classList.remove('dragging');
        // Re-baseline direction tracking so the next scroll continues from this state
        // instead of fighting it (a later scroll only flips on a fresh directional move).
        this.lastOffset = null;
        this.scrollAccum = 0;
        this.snapTo(collapsed ? 1 : 0);
    }

    private onHeaderClassChange() {
        const isExpanded = this.isAvatarExpanded;
        if (this.wasAvatarExpanded && !isExpanded)
            this.lastScroller = null; // full-screen avatar closed => re-baseline on next scroll
        this.wasAvatarExpanded = isExpanded;
    }

    private onScroll(e: Event) {
        // Ignore scroll while a drag/snap (and the layout shift it triggers) plays out.
        if (this.isSnapping || this.isDragging)
            return;
        // The full-screen avatar does not react to scroll at all — close it by tapping.
        if (this.isAvatarExpanded)
            return;

        const scroller = e.target;
        if (!(scroller instanceof HTMLElement))
            return;
        if (ScreenSize.isWide()) {
            this.setState(false, false);
            return;
        }

        // Fresh scroller (tab switch / after full-screen close): start expanded, never
        // collapse on its own.
        if (scroller !== this.lastScroller) {
            this.lastScroller = scroller;
            this.lastOffset = this.topOffset(scroller);
            this.scrollAccum = 0;
            this.setState(false, false);
            return;
        }

        // Direction-based: scrolling down collapses, scrolling up expands. This composes
        // with the drag (same axis) without the position-based fighting that flipped the
        // state back and forth.
        const offset = this.topOffset(scroller);
        if (offset === null) {
            this.lastOffset = null; // top out of the rendered window — re-baseline on return
            return;
        }
        if (offset <= EXPAND_AT_PX) {
            this.lastOffset = offset;
            this.scrollAccum = 0;
            this.setState(false, true); // always expanded at the very top
            return;
        }
        if (this.lastOffset === null) {
            this.lastOffset = offset; // re-baseline after a drag or an unknown stretch
            return;
        }

        const delta = offset - this.lastOffset;
        this.lastOffset = offset;
        if (delta * this.scrollAccum < 0)
            this.scrollAccum = 0; // direction reversal
        this.scrollAccum += delta;

        if (this.scrollAccum >= DIR_TRIGGER_PX && this.canCollapse(scroller)) {
            this.scrollAccum = 0;
            this.setState(true, true);
        }
        else if (this.scrollAccum <= -DIR_TRIGGER_PX) {
            this.scrollAccum = 0;
            this.setState(false, true);
        }
    }

    // Set the binary state. Animate the transition unless `animate` is false (instant jump,
    // used for tab switches and wide screens).
    private setState(collapsed: boolean, animate: boolean) {
        const target = collapsed ? 1 : 0;
        if (this.collapsed === collapsed && this.progress === target)
            return;

        this.collapsed = collapsed;
        if (animate) {
            this.snapTo(target);
        }
        else {
            cancelAnimationFrame(this.snapRaf);
            this.snapRaf = 0;
            this.rightPanel.classList.remove('snapping');
            this.setProgress(target);
        }
    }

    // Frame-driven snap to 0/1. `snapping` kills the dependent properties' own transitions
    // so the avatar and header move in lockstep, and makes onScroll bail so the layout
    // shift the collapse causes can't restart the animation.
    private snapTo(target: number) {
        cancelAnimationFrame(this.snapRaf);
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
    private canCollapse(scroller: HTMLElement): boolean {
        const scrollController = (scroller as unknown as ScrollLimitsSource).scrollController;
        if (scrollController) {
            const { min, max } = scrollController.getEffectiveScrollLimits();
            if (!Number.isFinite(min) || !Number.isFinite(max))
                return true;
            return (max - min) > this.minOverflowPx;
        }
        return scroller.scrollHeight - scroller.clientHeight > this.minOverflowPx;
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
                this.panel.setProgress(this.progress);
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
