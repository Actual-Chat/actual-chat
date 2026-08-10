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
// An existing item that resizes within this window of first appearing is a layout-shift offender
// (it didn't reserve its final height up-front); later resizes are usually genuine edits.
const LateResizeMs = 2500;

// What a live VirtualList exposes to the checker. Kept structural so the debug module stays
// decoupled from the (large) VirtualList class; the instance is passed in via an `any` cast.
export interface VirtualListDebugTarget {
    readonly identity: string;
    readonly defaultEdge: VirtualListEdge;
    readonly isContainerRevealed: boolean;
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

// A compact structural snapshot of a list item at a given measurement, for diffing what changed
// between the first (pre-settle) and final layout. `rows` lists descendants with their heights.
export interface ItemFingerprint {
    h: number;
    html: string;
    rows: { tag: string; cls: string; h: number }[];
}

// A post-initial-measurement height change of a list item — i.e. a message that didn't settle on
// its final height at first measurement. `late` ones (soon after the item appeared) are the offenders.
// `before`/`after` capture the item's structure at the previous and current measurement.
export interface ItemResize {
    key: string;
    chatEntryId: string | null;
    kind: string;
    from: number;
    to: number;
    delta: number;
    ageMs: number;
    late: boolean;
    at: number;
    before: ItemFingerprint;
    after: ItemFingerprint;
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
    private readonly itemResizes: ItemResize[] = [];
    private readonly lastMeasure = new Map<string, { size: number; fp: ItemFingerprint }>();
    private lastLoggedSig = '';
    private lastLoggedAt = 0;
    private lastResizeLogKey = '';
    private lastResizeLogAt = 0;
    // No-jump tracking: a keyed item's viewport-relative position across consecutive checks.
    private lastAnchor: { key: string; top: number; scrollTop: number; time: number } | null = null;
    private lastScroll: { scrollTop: number; scrollHeight: number; time: number } | null = null;
    private lastRendering = false;
    // Anchor captured at render start (before Blazor mutates nodes). Capturing later — inside
    // syncLayoutAfterRender — sees the DOM already shifted by inserted items but not yet compensated
    // by the container.top write, so the bracket reported that never-painted intermediate state as a
    // jump. One-shot: consumed by the render that follows.
    private preRenderAnchor: { key: string; top: number } | null = null;

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
        if (!this.vl.isContainerRevealed)
            return []; // initial placement hasn't landed yet — the wrapper is still visibility:hidden
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
        const clamp = this.checkScrollClamp(s);
        if (clamp)
            found.push(clamp);
        for (const v of found)
            this.report(v);

        return found;
    }

    public noteRenderStart(anchor: { key: string; top: number } | null): void {
        this.preRenderAnchor = anchor;
    }

    public takePreRenderAnchor(): { key: string; top: number } | null {
        const anchor = this.preRenderAnchor;
        this.preRenderAnchor = null;
        return anchor;
    }

    // A wrapper resize that leaves scrollTop past its new maximum makes the browser clamp it. Content
    // and scroll then move together, so anchor-jump's Δtop + Δscroll is exactly 0 and it cannot see
    // this — yet the user sees the list jump backwards. This check is the only one that catches it.
    private checkScrollClamp(s: VlSnapshot): VlViolation | null {
        const prev = this.lastScroll;
        this.lastScroll = { scrollTop: s.scrollTop, scrollHeight: s.scrollHeight, time: s.time };
        if (!prev || s.isRendering || s.time - prev.time > 300)
            return null;

        // The signature is specific, and deliberately so: the list has several scroll-write paths and
        // only one of them stamps lastProgrammaticScrollAt, so "scroll moved on its own" alone is far
        // too noisy. A clamp is the scroll range shrinking out from under the viewport, which leaves
        // scrollTop pulled back and pinned at the new maximum.
        const dHeight = s.scrollHeight - prev.scrollHeight;
        const dScroll = s.scrollTop - prev.scrollTop;
        if (dHeight >= -JumpEps || dScroll >= -JumpEps)
            return null;
        if (Math.abs(s.scrollTop - (s.scrollHeight - s.clientHeight)) > JumpEps)
            return null; // not pinned at the new max — something else moved the scroll

        return {
            code: 'scroll-clamp',
            message: `scrollHeight shrank ${Math.round(dHeight)}px under the viewport, pulling scrollTop back ${Math.round(-dScroll)}px`,
            detail: {
                dScroll: Math.round(dScroll),
                dHeight: Math.round(dHeight),
                scrollTop: Math.round(s.scrollTop),
                scrollHeight: s.scrollHeight,
            },
            snapshot: s,
        };
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

    // Called by VirtualList on every item measurement. Snapshots the item's structure each time and,
    // when the size changed vs the previous measurement, records a resize carrying the before/after
    // snapshots — so the pre-settle (first-pass) DOM that caused the height change is captured.
    public noteItemMeasure(key: string, size: number, createdAt: number, el: HTMLElement): void {
        const fp = itemFingerprint(el);
        const prev = this.lastMeasure.get(key);
        this.lastMeasure.set(key, { size, fp });
        if (!prev || prev.size === size)
            return;

        const ageMs = Math.max(0, Date.now() - createdAt);
        const r: ItemResize = {
            key,
            chatEntryId: el.querySelector<HTMLElement>('[data-chat-entry-id]')?.dataset.chatEntryId
                ?? el.dataset.chatEntryId ?? null,
            kind: classifyItemContent(el),
            from: prev.size,
            to: size,
            delta: size - prev.size,
            ageMs: Math.round(ageMs),
            late: ageMs < LateResizeMs,
            at: Math.round(performance.now()),
            before: prev.fp,
            after: fp,
        };
        this.itemResizes.push(r);
        if (this.itemResizes.length > 500)
            this.itemResizes.shift();
        // Deduped console log: same key within 1s logs once.
        if (key === this.lastResizeLogKey && r.at - this.lastResizeLogAt < 1000)
            return;

        this.lastResizeLogKey = key;
        this.lastResizeLogAt = r.at;
        const sign = r.delta >= 0 ? '+' : '';
        const tag = r.late ? ' LATE' : '';
        warnLog?.log(
            `⤡ resize "${key}" ${r.from}→${r.to} ${sign}${r.delta}px +${r.ageMs}ms${tag} ${r.kind}`);
    }

    public get itemResizeList(): ItemResize[] { return this.itemResizes; }
    public clearItemResizes(): void { this.itemResizes.length = 0; }

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
            isInfinite: true,
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

// Compact structural snapshot of an item: its height, a trimmed outerHTML, and a depth-first list
// of descendants with heights (capped) — enough to diff which sub-block changed height.
function itemFingerprint(el: HTMLElement): ItemFingerprint {
    const rows: { tag: string; cls: string; h: number }[] = [];
    const walk = (node: Element) => {
        for (const c of Array.from(node.children)) {
            if (rows.length >= 30)
                return;
            const r = c.getBoundingClientRect();
            if (r.height > 0) {
                const cls = (c.getAttribute('class') ?? '').slice(0, 40);
                rows.push({ tag: c.tagName.toLowerCase(), cls, h: Math.round(r.height) });
            }
            walk(c);
        }
    };
    walk(el);
    return {
        h: Math.round(el.getBoundingClientRect().height),
        html: el.outerHTML.replace(/\s+/g, ' ').slice(0, 500),
        rows,
    };
}

// Best-effort content classification for a list item, for the resize tracker's `kind` field.
function classifyItemContent(el: HTMLElement): string {
    if (el.querySelector('.image-attachment'))
        return 'image';
    if (el.querySelector('.video-attachment, video'))
        return 'video';
    if (el.querySelector('.file-attachment'))
        return 'file';
    if (el.querySelector('.link-preview, link-preview'))
        return 'link-preview';
    if (el.querySelector('.audio-attachment, audio-player, audio'))
        return 'audio';
    if ((el.dataset.key ?? '').endsWith('-date-line'))
        return 'date-line';
    if (el.classList.contains('group') || el.querySelector('.chat-message-group'))
        return 'message-group';

    return 'text';
}

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
//
// The bottom is measured against the DOM, the top against itemRange, because only the bottom is
// capped: CutVirtualSpaceAtBottom sizes the wrapper to itemRange.end + endAnchorSize, so
// (scrollTop + clientHeight) - contentBottom is identically <= 0 in model coordinates and a
// model-side bottom check can never fire. The gap the user sees there is the model overshooting
// the DOM - the newest item's bottom resting above the viewport with no scroll left to close it.
function voidPastEdge(vl: VirtualListDebugTarget, s: VlSnapshot): VlViolation | null {
    if (s.isScrolling || s.isRendering || s.itemRange == null)
        return null;

    const contentTop = s.itemRange[0];
    const contentBottom = s.itemRange[1] + vl.state.endAnchorSize;
    const tall = contentBottom - contentTop > s.clientHeight;
    const isAtBottom = (s.scrollHeight - s.clientHeight) - s.scrollTop <= Eps;
    const voidBelow = s.lastContentBottom == null
        ? 0
        : s.clientHeight - vl.state.endAnchorSize - s.lastContentBottom;
    const voidAbove = contentTop - s.scrollTop;
    if (s.hasLast && (tall || vl.defaultEdge === VirtualListEdge.End) && isAtBottom && voidBelow > GapEps)
        return {
            code: 'void-below-newest',
            message: `settled ${Math.round(voidBelow)}px past the newest item`,
            detail: {
                voidBelow: Math.round(voidBelow),
                scrollTop: s.scrollTop,
                lastContentBottom: s.lastContentBottom,
                modelContentBottom: Math.round(contentBottom),
            },
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
