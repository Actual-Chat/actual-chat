import { NumberRange } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VirtualList');

// Console-debugging handles exposed on the global object.
type VlDebugGlobal = typeof globalThis & {
    __vlDebugs?: Record<string, VirtualListDebug>;
    __vlDebug?: VirtualListDebug;
};

// Pixel tolerances. Geometry is integer-floored in several places, and measured item sizes
// shift by a few px during reflow, so small deviations are not real inconsistencies.
const Eps = 8;
const GapEps = 24;
const JumpEps = 12;

// What a live VirtualList exposes to the checker. Kept structural so the debug module stays
// decoupled from the (large) VirtualList class; the instance is passed in via an `any` cast.
export interface VirtualListDebugTarget {
    readonly identity: string;
    readonly defaultEdge: VirtualListEdge;
    readonly isInfinite: boolean;
    readonly ref: HTMLElement;
    readonly wrapperRef: HTMLElement;
    readonly containerRef: HTMLElement;
    readonly spacerRef: HTMLElement;
    readonly endSpacerRef: HTMLElement;
    readonly isRendering: boolean;
    readonly statistics: { itemSize: number };
    readonly state: {
        itemRange?: NumberRange | null;
        viewport?: NumberRange | null;
        isScrolling: boolean;
        endAnchorSize: number;
        renderState: { renderIndex: number; hasVeryFirstItem: boolean; hasVeryLastItem: boolean };
    };
}

export interface VlViolation {
    code: string;
    message: string;
    detail: Record<string, unknown>;
    snapshot: VlSnapshot;
}

export interface VlSnapshot {
    time: number;
    trigger: string;
    identity: string;
    scrollTop: number;
    scrollHeight: number;
    clientHeight: number;
    isRendering: boolean;
    isScrolling: boolean;
    isInfinite: boolean;
    renderIndex: number;
    hasFirst: boolean;
    hasLast: boolean;
    statItemSize: number;
    itemRange: [number, number] | null;
    viewport: [number, number] | null;
    containerTop: string;
    wrapperHeight: number;
    spacerStartH: number;
    spacerStartShown: boolean;
    spacerEndH: number;
    spacerEndShown: boolean;
    contentCount: number;
    firstContentTop: number | null;   // viewport-relative px
    lastContentBottom: number | null;
    domContentExtent: number | null;
    coveredInViewport: number;
}

interface Geom {
    vr: DOMRect;
    contentRects: { top: number; bottom: number; h: number }[]; // viewport-relative
    spacerRects: { which: 'start' | 'end'; top: number; bottom: number; h: number }[];
}

// One discrepancy detector — pure read, returns a violation or null.
type Check = (vl: VirtualListDebugTarget, s: VlSnapshot, geom: Geom) => VlViolation | null;

export class VirtualListDebug {
    private readonly vl: VirtualListDebugTarget;
    private timer: ReturnType<typeof setInterval> | null = null;
    private lastRequestSnapshot: VlSnapshot | null = null;
    private readonly recent: VlViolation[] = [];
    private lastLoggedSig = '';
    private lastLoggedAt = 0;
    // No-jump tracking: a keyed item's viewport-relative position across consecutive checks.
    private lastAnchor: { key: string; top: number; scrollTop: number; time: number } | null = null;
    private lastRendering = false;

    public static readonly checks: Record<string, Check> = {
        blankViewport,
        viewportGap,
        voidPastEdge,
        scrollTopOutOfRange,
    };

    constructor(vl: VirtualListDebugTarget) {
        this.vl = vl;
    }

    public start(intervalMs = 100): void {
        if (this.timer != null)
            return;
        this.timer = setInterval(() => this.check('timer'), intervalMs);
        const g = globalThis as VlDebugGlobal;
        const debugs = (g.__vlDebugs ??= {});
        debugs[this.vl.identity] = this;
        g.__vlDebug = this; // most-recently-started, convenience handle
        warnLog?.log(`[${this.vl.identity}] debug checker started`);
    }

    public stop(): void {
        if (this.timer != null) {
            clearInterval(this.timer);
            this.timer = null;
        }
        const g = globalThis as VlDebugGlobal;
        if (g.__vlDebugs)
            // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
            delete g.__vlDebugs[this.vl.identity];
        if (g.__vlDebug === this)
            delete g.__vlDebug;
    }

    // Called right after a data request is sent, so violations can show the geometry that
    // produced the request that (likely) led to them.
    public onRequestData(): void {
        this.lastRequestSnapshot = this.capture('requestData');
        this.check('requestData');
    }

    public onEvent(trigger: string): void {
        this.check(trigger);
    }

    public check(trigger: string): VlViolation[] {
        if (this.vl.ref.clientHeight === 0)
            return []; // hidden / not laid out
        const s = this.capture(trigger);
        const geom = this.readGeom();
        const found: VlViolation[] = [];
        for (const name of Object.keys(VirtualListDebug.checks)) {
            let v: VlViolation | null = null;
            try { v = VirtualListDebug.checks[name](this.vl, s, geom); }
            catch { /* transient null range mid-render — ignore */ }
            if (v)
                found.push(v);
        }
        const jump = this.checkAnchorJump(s);
        if (jump)
            found.push(jump);
        for (const v of found)
            this.report(v);

        return found;
    }

    // No-jump invariant: between two consecutive checks a keyed item that is still on screen must
    // move exactly opposite to scrollTop (Δtop === -Δscroll). Any extra movement means the chain was
    // re-laid-out without compensating scrollTop — i.e. a visible jump while scrolling/anchoring.
    private checkAnchorJump(s: VlSnapshot): VlViolation | null {
        const anchor = this.pickAnchor();
        const prev = this.lastAnchor;
        let v: VlViolation | null = null;
        if (anchor && prev && prev.key === anchor.key
            && !s.isRendering && !this.lastRendering
            && s.time - prev.time < 300) {
            const dTop = anchor.top - prev.top;
            const dScroll = s.scrollTop - prev.scrollTop;
            const drift = dTop + dScroll; // expected: dTop === -dScroll  ⇒  drift === 0
            if (Math.abs(drift) > JumpEps)
                v = {
                    code: 'anchor-jump',
                    message: `anchored item "${anchor.key}" jumped ${Math.round(drift)}px (moved ${Math.round(dTop)}px vs scroll ${Math.round(-dScroll)}px)`,
                    detail: { key: anchor.key, dTop: Math.round(dTop), dScroll: Math.round(dScroll), drift: Math.round(drift) },
                    snapshot: s,
                };
        }
        this.lastAnchor = anchor ? { key: anchor.key, top: anchor.top, scrollTop: s.scrollTop, time: s.time } : null;
        this.lastRendering = s.isRendering;
        return v;
    }

    // The keyed content item whose centre is closest to the viewport centre (a stable anchor).
    private pickAnchor(): { key: string; top: number } | null {
        const vr = this.vl.ref.getBoundingClientRect();
        const centre = vr.height / 2;
        let best: { key: string; top: number } | null = null;
        let bestDist = Infinity;
        for (const li of Array.from(this.vl.containerRef.querySelectorAll<HTMLElement>('li.item[data-key], .group .item[data-key]'))) {
            // Skip data-skip and sticky rows (e.g. the pinned conversation header): a correctly-pinned
            // sticky element reads as a jump to this scroll-relative check (drift === dScroll), a false
            // positive. The real anchoring already excludes these from pivots.
            if (li.dataset.skip === 'true' || window.getComputedStyle(li).position === 'sticky')
                continue;
            const r = li.getBoundingClientRect();
            if (r.height <= 0 || r.bottom <= vr.top || r.top >= vr.bottom)
                continue;
            const top = r.top - vr.top;
            const dist = Math.abs(top + r.height / 2 - centre);
            if (dist < bestDist) {
                bestDist = dist;
                best = { key: li.dataset.key ?? '', top };
            }
        }
        return best;
    }

    // Called by VirtualList when a render moved a visible anchored item that should have stayed put
    // (a jump). Detection lives in the render itself (it can bracket the layout write); this only
    // records the finding into the same buffer/log as the other checks.
    public noteRenderJump(detail: Record<string, unknown>): void {
        this.report({
            code: 'render-jump',
            message: `render moved anchored item "${String(detail.key)}" by ${String(detail.drift)}px (no intentional scroll)`,
            detail,
            snapshot: this.capture('render'),
        });
    }

    public get violations(): VlViolation[] { return this.recent; }
    public clear(): void { this.recent.length = 0; }
    public get lastRequest(): VlSnapshot | null { return this.lastRequestSnapshot; }

    private report(v: VlViolation): void {
        this.recent.push(v);
        if (this.recent.length > 200)
            this.recent.shift();
        // Dedup console spam: same code+detail within 1s logs once.
        const sig = v.code + JSON.stringify(v.detail);
        const now = v.snapshot.time;
        if (sig === this.lastLoggedSig && now - this.lastLoggedAt < 1000)
            return;
        this.lastLoggedSig = sig;
        this.lastLoggedAt = now;
        warnLog?.log(`⚠ ${v.code} [${v.snapshot.trigger}]: ${v.message}`, v.detail, v.snapshot);
    }

    private readGeom(): Geom {
        const el = this.vl.ref;
        const vr = el.getBoundingClientRect();
        const contentRects: Geom['contentRects'] = [];
        for (const li of Array.from(this.vl.containerRef.querySelectorAll<HTMLElement>('li.item, li.group'))) {
            const r = li.getBoundingClientRect();
            if (r.height <= 0)
                continue;
            contentRects.push({ top: r.top - vr.top, bottom: r.bottom - vr.top, h: r.height });
        }
        const spacerRects: Geom['spacerRects'] = [];
        const spacers: [('start' | 'end'), HTMLElement][] = [['start', this.vl.spacerRef], ['end', this.vl.endSpacerRef]];
        for (const [which, ref] of spacers) {
            const r = ref.getBoundingClientRect();
            if (getComputedStyle(ref).display !== 'none' && r.height > 0)
                spacerRects.push({ which, top: r.top - vr.top, bottom: r.bottom - vr.top, h: r.height });
        }
        // The end anchor (a small fixed element at the End edge; 48px on narrow screens) legitimately
        // occupies the bottom of the viewport — count it as covering so it isn't read as a gap.
        const anchor = this.vl.wrapperRef.querySelector<HTMLElement>('.c-end-anchor');
        if (anchor) {
            const r = anchor.getBoundingClientRect();
            if (r.height > 0)
                spacerRects.push({ which: 'end', top: r.top - vr.top, bottom: r.bottom - vr.top, h: r.height });
        }
        return { vr, contentRects, spacerRects };
    }

    private capture(trigger: string): VlSnapshot {
        const vl = this.vl;
        const el = vl.ref;
        const vr = el.getBoundingClientRect();
        const ir = vl.state.itemRange;
        const vp = vl.state.viewport;
        const content = Array.from(vl.containerRef.querySelectorAll<HTMLElement>('li.item, li.group'))
            .map(li => li.getBoundingClientRect()).filter(r => r.height > 0);
        const firstTop = content.length ? Math.round(content[0].top - vr.top) : null;
        const lastBottom = content.length ? Math.round(content[content.length - 1].bottom - vr.top) : null;
        const covered = coveredPx(vr.height, content.map(r => ({ top: r.top - vr.top, bottom: r.bottom - vr.top })));
        return {
            time: Math.round(performance.now()),
            trigger,
            identity: vl.identity,
            scrollTop: Math.round(el.scrollTop),
            scrollHeight: el.scrollHeight,
            clientHeight: el.clientHeight,
            isRendering: vl.isRendering,
            isScrolling: vl.state.isScrolling,
            isInfinite: vl.isInfinite,
            renderIndex: vl.state.renderState.renderIndex,
            hasFirst: vl.state.renderState.hasVeryFirstItem,
            hasLast: vl.state.renderState.hasVeryLastItem,
            statItemSize: Math.round(vl.statistics.itemSize),
            itemRange: ir ? [Math.round(ir.start), Math.round(ir.end)] : null,
            viewport: vp ? [Math.round(vp.start), Math.round(vp.end)] : null,
            containerTop: vl.containerRef.style.top,
            wrapperHeight: Math.round(parseFloat(vl.wrapperRef.style.height) || vl.wrapperRef.offsetHeight),
            spacerStartH: Math.round(vl.spacerRef.getBoundingClientRect().height),
            spacerStartShown: getComputedStyle(vl.spacerRef).display !== 'none',
            spacerEndH: Math.round(vl.endSpacerRef.getBoundingClientRect().height),
            spacerEndShown: getComputedStyle(vl.endSpacerRef).display !== 'none',
            contentCount: content.length,
            firstContentTop: firstTop,
            lastContentBottom: lastBottom,
            domContentExtent: firstTop != null && lastBottom != null ? lastBottom - firstTop : null,
            coveredInViewport: Math.round(covered),
        };
    }
}

// Helpers

// Total px of [0, height] covered by the given (viewport-relative) intervals.
function coveredPx(height: number, intervals: { top: number; bottom: number }[]): number {
    const clipped = intervals
        .map(i => ({ top: Math.max(0, i.top), bottom: Math.min(height, i.bottom) }))
        .filter(i => i.bottom > i.top)
        .sort((a, b) => a.top - b.top);
    let covered = 0, cursor = -1;
    for (const i of clipped) {
        const top = Math.max(i.top, cursor);
        if (i.bottom > top)
            covered += i.bottom - top;
        cursor = Math.max(cursor, i.bottom);
    }
    return covered;
}

// First uncovered sub-range of [0, height] given the covered intervals (or null if covered).
function firstGap(height: number, intervals: { top: number; bottom: number }[]): NumberRange | null {
    const clipped = intervals
        .map(i => ({ top: Math.max(0, i.top), bottom: Math.min(height, i.bottom) }))
        .filter(i => i.bottom > i.top)
        .sort((a, b) => a.top - b.top);
    let cursor = 0;
    for (const i of clipped) {
        if (i.top - cursor > GapEps)
            return new NumberRange(cursor, i.top);
        cursor = Math.max(cursor, i.bottom);
    }
    if (height - cursor > GapEps)
        return new NumberRange(cursor, height);

    return null;
}

// The viewport must show something (content or a loadable skeleton-spacer). If content is loaded
// but none of it — and no visible spacer — intersects the viewport, the user sees blank wrapper.
function blankViewport(_vl: VirtualListDebugTarget, s: VlSnapshot, geom: Geom): VlViolation | null {
    if (s.contentCount === 0)
        return null;
    const anything = geom.contentRects.length > 0 || geom.spacerRects.length > 0;
    const touches = [...geom.contentRects, ...geom.spacerRects].some(r => r.bottom > 0 && r.top < geom.vr.height);
    if (anything && touches)
        return null;
    return {
        code: 'blank-viewport',
        message: 'viewport shows neither content nor a skeleton-spacer (blank screen)',
        detail: { contentInViewport: geom.contentRects.filter(r => r.bottom > 0 && r.top < geom.vr.height).length },
        snapshot: s,
    };
}

// A hole between rendered elements inside the viewport (partial blank).
function viewportGap(_vl: VirtualListDebugTarget, s: VlSnapshot, geom: Geom): VlViolation | null {
    if (s.contentCount === 0)
        return null;
    // A list shorter than its viewport legitimately leaves empty space at its non-anchored edge
    // (e.g. a 3-item sidebar). Only content taller than the viewport should fully cover it.
    if (s.domContentExtent != null && s.domContentExtent < geom.vr.height)
        return null;
    const gap = firstGap(geom.vr.height, [...geom.contentRects, ...geom.spacerRects]);
    if (gap == null)
        return null;
    return {
        code: 'viewport-gap',
        message: `uncovered ${Math.round(gap.size)}px hole in viewport [${Math.round(gap.start)}, ${Math.round(gap.end)}]`,
        detail: { gapStart: Math.round(gap.start), gapEnd: Math.round(gap.end), gapSize: Math.round(gap.size) },
        snapshot: s,
    };
}

// Infinite-list rubber-band invariant: once settled, the list must not rest with a gap past a
// discovered edge. The bottom must be flush when End-preferred (or content is taller than the
// viewport); the top must be flush when Start-preferred (or taller). The non-preferred edge of a
// short fully-loaded list legitimately has a gap, so it is not checked.
function voidPastEdge(vl: VirtualListDebugTarget, s: VlSnapshot): VlViolation | null {
    if (!s.isInfinite || s.isScrolling || s.isRendering || s.itemRange == null)
        return null;

    const contentTop = s.itemRange[0];
    const contentBottom = s.itemRange[1] + vl.state.endAnchorSize;
    const tall = contentBottom - contentTop > s.clientHeight;
    const voidBelow = (s.scrollTop + s.clientHeight) - contentBottom;
    const voidAbove = contentTop - s.scrollTop;
    if (s.hasLast && (tall || vl.defaultEdge === VirtualListEdge.End) && voidBelow > GapEps)
        return {
            code: 'void-below-newest',
            message: `settled ${Math.round(voidBelow)}px past the newest item`,
            detail: { voidBelow: Math.round(voidBelow), scrollTop: s.scrollTop, contentBottom: Math.round(contentBottom) },
            snapshot: s,
        };
    if (s.hasFirst && (tall || vl.defaultEdge === VirtualListEdge.Start) && voidAbove > GapEps)
        return {
            code: 'void-above-oldest',
            message: `settled ${Math.round(voidAbove)}px past the oldest item`,
            detail: { voidAbove: Math.round(voidAbove), scrollTop: s.scrollTop, contentTop: Math.round(contentTop) },
            snapshot: s,
        };

    return null;
}

function scrollTopOutOfRange(_vl: VirtualListDebugTarget, s: VlSnapshot): VlViolation | null {
    const maxTop = s.scrollHeight - s.clientHeight;
    if (s.scrollTop < -Eps || s.scrollTop > maxTop + Eps)
        return {
            code: 'scrolltop-out-of-range',
            message: `scrollTop ${s.scrollTop} outside [0, ${Math.round(maxTop)}]`,
            detail: { scrollTop: s.scrollTop, maxTop: Math.round(maxTop) },
            snapshot: s,
        };
    return null;
}
