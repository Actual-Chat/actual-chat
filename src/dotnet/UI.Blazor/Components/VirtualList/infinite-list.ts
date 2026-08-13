/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition */
import { debounce, PromiseSource, PromiseSourceWithTimeout, throttle } from 'actuallab-core';
import { NumberRange, Range } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { VirtualListDebug, VirtualListDebugTarget } from './virtual-list-debug';
import { Pivot } from './ts/pivot';
import { ScrollController } from 'scroll-controller';
import { DotNet } from '@microsoft/dotnet-js-interop';

import { getLogs } from 'logging';
import { fastRaf, fastReadRafAsync } from 'fast-raf';
import { DeviceInfo } from 'device-info';
import { clamp } from 'math';
import { BrowserInfo } from '../../Services/BrowserInfo/browser-info';
import { DocumentEvents } from 'event-handling';
import { type Subscription } from 'rxjs';

const { warnLog, debugLog } = getLogs('InfiniteList');

const UpdateViewportInterval = 64;
const UpdateItemVisibilityInterval = 250;
const VisibilityEpsilon = 4;
const EdgeEpsilon = 4;
const ScrollDebounce = 200;
const InteractivePivotTtlMs = 2000;
const ProgrammaticScrollGuardMs = DeviceInfo.isMobile ? 250 : 100;
const SkeletonDetectionBoundary = 200;
const MinViewPortSize = 400;
const RequestDataTimeout = 800;
// Initial-reveal watch: px tolerance for "preferred edge is flush", and the hard backstop after which
// the wrapper is revealed regardless (e.g. an empty chat that never "places").
const EdgePlacedEpsilon = 8;
const RevealTimeout = 1500;
// Min edge re-pin distance. A re-pin re-derives the target scrollTop from the DOM; when already flush
// that target sits ~1 device px off the current scrollTop on fractional-DPI screens (scrollTop quantizes
// to integer CSS px, which isn't a whole device px at e.g. dpr 2.5). Writing it re-snaps the scroll and
// flips it ±1px on every re-pin — a visible jitter on each render that re-pins (e.g. a late avatar load).
// The residual is unclosable, so skip re-pins below 1px; a real edge change is many px.
const EdgeRepinEpsilon = 1;
// Max nudge passes per edge re-pin. In infinite mode container.top is recomputed on scroll, so 1 scrollTop
// unit != 1px near the edge (the ratio drifts ~1.75x) and a single nudge undershoots. Each pass closes most
// of the residual, so 3 lands sub-pixel; aiming at the model edge instead would strand the list whenever the
// model over-counts the tail (e.g. a just-removed placeholder).
const EdgeRepinMaxPasses = 3;
// Fixed scroll size of an infinite (scrollbar-less) list — its wrapper height. Kept well under the
// browser's max element height (Firefox ≈ 17.9M); at ~50px/item that is still ~200k items of scroll.
// Must match InfiniteSize in InfiniteList.razor.cs.
const InfiniteSize = 1e7;
// Min viewport-to-container gap for repinIfViewportStranded: the blank leaves a ~InfiniteSize/2 gap,
// while legit scroll/overscroll gaps are far smaller — so no false positives.
const StrandedGapThreshold = InfiniteSize / 10;
// When true, the wrapper is trimmed to the newest once the last item is loaded (native hard-stop at the
// bottom). When false, the bottom is managed like the top — via a scroll-limit + rubber-band overscroll.
const CutVirtualSpaceAtBottom = true;

type ScrollToEdgeReason = 'no-pivot' | 'last-item' | 'item' | 'sticky-edge' | 'non-item-resize' | 'item-resize' | 'viewport-resize' | 'stranded' | 'unknown';
interface ScrollIntent {
    shouldUseSmoothScroll: boolean;
    reason: ScrollToEdgeReason;
    scroll?: () => void;
}

interface VirtualListState {
    // Scroll
    viewport: NumberRange | null;
    lastViewport: NumberRange | null;
    isScrolling: boolean;
    scrollTime: number | null;
    scrollDirection: 'up' | 'down' | 'none';
    windowScrollTop: number;
    // Items
    orderedItems: VirtualListItem[];
    itemRange: NumberRange | null;
    pivots: Pivot[];
    // Range tracking
    minStart: number | null;
    isStartKnown: boolean;
    maxEnd: number | null;
    isEndKnown: boolean;
    // Query
    query: VirtualListDataQuery;
    lastQuery: VirtualListDataQuery;
    lastQueryTime: number | null;
    // Render
    renderState: VirtualListRenderState;
    renderStartedAt: number | null;
    renderCompletedAt: number;
    lastProgrammaticScrollAt: number;
    // Visibility
    isNearSkeleton: boolean;
    isEndAnchorVisible: boolean;
    endAnchorSize: number;
    // UI
    stickyEdge: Required<VirtualListStickyEdgeState> | null;
    isUpdatingPivots: boolean;
}

interface VirtualListStateSnapshot {
    readonly reason: string;
    readonly time: number;
    readonly state: Readonly<VirtualListState>;
    readonly changedFields: readonly (keyof VirtualListState)[];
}

const StateHistoryCapacity = 50;
const SkeletonWatchdogInterval = 5000;

const delayMs = (ms: number): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

export class InfiniteList {
    public static enableWatchdogFixes = false;
    public static enableDebug = false;
    // Debug-only: when > 0, inject a random [0, value) ms delay before each render (debugRenderDelayMs)
    // and each data request (debugDataLoadDelayMs) to surface scroll/recenter timing races. 0 in prod.
    public static debugRenderDelayMs = 0;
    public static debugDataLoadDelayMs = 0;
    private static readonly _instances = new Set<InfiniteList>();

    public static dumpStateChangeLogs(lastN?: number, endStateEvery = 10): void {
        for (const instance of InfiniteList._instances) {
            console.warn(
                `[InfiniteList:${instance.identity}] State history:`,
                instance.getStateChangeLog(lastN, endStateEvery));
        }
    }

    // On-demand consistency checker. Off by default; turn on via debugUI.virtualListDebug(true)
    // or globalThis.InfiniteList.setDebugEnabled(true). Toggles every live list and all new ones.
    public static setDebugEnabled(enabled: boolean): VirtualListDebug[] {
        InfiniteList.enableDebug = enabled;
        const result: VirtualListDebug[] = [];
        for (const instance of InfiniteList._instances) {
            if (enabled) {
                instance.startDebug();
                if (instance.debug)
                    result.push(instance.debug);
            }
            else {
                instance.debug?.stop();
                instance.debug = null;
            }
        }
        return result;
    }

    /** ref to div.virtual-list */
    private readonly createdAt: number;
    private isContainerRevealed = false;
    private readonly ref: HTMLElement;
    private readonly containerRef: HTMLElement;
    private readonly renderStateRef: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly identity: string;
    private readonly defaultEdge: VirtualListEdge;
    private readonly defaultSpacerSize: number;
    private readonly expandMultiplier: number;
    private readonly retainedItemCount: number;
    private readonly scrollController: ScrollController;
    private readonly wrapperRef: HTMLElement;
    private readonly spacerRef: HTMLElement;
    private readonly endSpacerRef: HTMLElement;
    private readonly renderIndexRef: HTMLElement;
    private readonly endAnchorRef: HTMLElement;
    private readonly abortController: AbortController;
    private readonly itemSetChangeObserver: MutationObserver;
    private readonly sizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private readonly skeletonObserver0: IntersectionObserver;
    private readonly skeletonObserver1: IntersectionObserver;
    private readonly unmeasuredItems: Set<string>;
    private readonly visibleItems: Set<string>;
    private readonly items: Map<string, VirtualListItem>;
    private readonly sizeCache: Map<string, number>;
    private readonly statistics: VirtualListStatistics = new VirtualListStatistics();
    private readonly keySortCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
    private readonly visibilityChangeSubscription: Subscription;
    private readonly rowGap: number = 2;

    private isDisposed = false;
    private cachedAllItemRefs: HTMLElement[] | null = null;
    private whenRequestDataCompleted: PromiseSource<void> | null = null;
    private turnOffScrollingCallback?: () => void;
    private isPointerDown = false;
    private lastObservedScrollTop: number | null = null;
    private skeletonWatchdogTimer: ReturnType<typeof setInterval> | null = null;
    private skeletonWatchdogLastVersion = -1;
    private userScrollDirection: 'up' | 'down' | 'none' = 'none';
    private debug: VirtualListDebug | null = null;

    private _state: VirtualListState;
    private readonly _stateHistory: VirtualListStateSnapshot[] = [];
    private _stateVersion = 0;

    private get state(): VirtualListState { return this._state; }

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        spacerSize: number,
        expandMultiplier: number,
        retainedItemCount = 5,
    ) {
        return new InfiniteList(
            ref,
            backendRef,
            identity,
            defaultEdge,
            spacerSize,
            expandMultiplier,
            retainedItemCount);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        spacerSize: number,
        expandMultiplier: number,
        retainedItemCount = 5,
    ) {
        if (debugLog) {
            debugLog?.log(`constructor`);
            globalThis.virtualList = this;
        }
        globalThis['InfiniteList'] = InfiniteList;

        this.createdAt = Date.now();
        this.ref = ref;
        this.blazorRef = backendRef;
        this.identity = identity;
        this.defaultEdge = defaultEdge;
        this.defaultSpacerSize = spacerSize;
        this.expandMultiplier = expandMultiplier;
        this.retainedItemCount = Math.max(1, retainedItemCount);

        this.items = new Map<string, VirtualListItem>();
        this.sizeCache = new Map<string, number>();

        this.isDisposed = false;
        this.abortController = new AbortController();
        this.wrapperRef = this.ref.querySelector(':scope > .c-wrapper')!;
        this.containerRef = this.wrapperRef.querySelector(':scope > .c-virtual-container')!;
        this.spacerRef = this.containerRef.querySelector(':scope > .c-spacer-start')!;
        this.endSpacerRef = this.containerRef.querySelector(':scope > .c-spacer-end')!;
        this.renderStateRef = this.ref.querySelector(':scope > .data.render-state')!;
        this.renderIndexRef = this.ref.querySelector(':scope > .data.render-index')!;
        this.endAnchorRef = this.containerRef.querySelector(':scope > .c-end-anchor')!;
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;
        this.wrapperRef.style.height = `${InfiniteSize}px`;
        this.scrollController = new ScrollController(
            this.ref, true, this.containerRef, () => this.computeScrollLimits());

        this._state = {
            viewport: null,
            lastViewport: null,
            isScrolling: false,
            scrollTime: null,
            scrollDirection: 'none',
            windowScrollTop: 0,
            orderedItems: [],
            itemRange: null,
            pivots: [],
            minStart: null,
            isStartKnown: false,
            maxEnd: null,
            isEndKnown: false,
            query: VirtualListDataQuery.None,
            lastQuery: VirtualListDataQuery.None,
            lastQueryTime: null,
            renderState: {
                renderIndex: -1,
                query: VirtualListDataQuery.None,
                keyRange: new Range<string>('', ''),
                beforeCount: null,
                afterCount: null,
                estimatedCount: null,
                count: 0,
                hasVeryFirstItem: false,
                hasVeryLastItem: false,
            },
            renderStartedAt: null,
            renderCompletedAt: 0,
            lastProgrammaticScrollAt: 0,
            isNearSkeleton: false,
            isEndAnchorVisible: false,
            endAnchorSize: this.endAnchorRef.getBoundingClientRect().height,
            stickyEdge: null,
            isUpdatingPivots: false,
        };

        // Always top-to-bottom: defaultEdge is now only a preference (initial scroll target + sticky
        // edge), not a coordinate flip. There is no reverse rendering anymore.
        this.ref.style.flexDirection = 'column';

        // Events & observers
        const listenerOptions = { signal: this.abortController.signal, passive: true, };
        this.ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this.ref.addEventListener('scrollend', this.onScrollEnd, listenerOptions);
        this.ref.addEventListener('pointerdown', this.onPointerDown, listenerOptions);
        this.ref.addEventListener('pointerup', this.onPointerUp, listenerOptions);
        this.ref.addEventListener('pointercancel', this.onPointerUp, listenerOptions);
        this.ref.addEventListener('wheel', this.onWheel, listenerOptions);
        this.itemSetChangeObserver = new MutationObserver(this.onItemSetChange);
        this.itemSetChangeObserver.observe(this.containerRef, { childList: true });
        this.itemSetChangeObserver.observe(this.renderIndexRef, { attributes: true });
        this.sizeObserver = new ResizeObserver(this.onResize);
        // Fire as early as possible on any intersection change; a 0 threshold doesn't work despite the docs.
        const visibilityThresholds = [...Array(101).keys()].map(i => i / 100);
        this.visibilityObserver = new IntersectionObserver(
            this.onItemVisibilityChange,
            {
                // Track visibility as intersection of virtual list viewport, not the window!
                root: this.ref,
                // Extend visibility outside of the viewport.
                rootMargin: `${VisibilityEpsilon}px`,
                threshold: visibilityThresholds,
            });
        this.skeletonObserver0 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this.ref,
                rootMargin: `-5px`,
                threshold: visibilityThresholds,
            });
        this.skeletonObserver1 = new IntersectionObserver(
            this.onSkeletonVisibilityChange,
            {
                root: this.ref,
                // Extend visibility outside of the viewport
                rootMargin: `${SkeletonDetectionBoundary}px`,
                threshold: visibilityThresholds,
            });

        this.unmeasuredItems = new Set<string>();
        this.visibleItems = new Set<string>();

        this.sizeObserver.observe(this.endAnchorRef, { box: 'border-box' });
        // Observe the viewport: a height-only change (keyboard, panels) resizes neither items nor the end
        // anchor, so without this the sticky edge isn't re-pinned and content drifts off-screen.
        this.sizeObserver.observe(this.ref, { box: 'border-box' });
        this.visibilityObserver.observe(this.endAnchorRef);
        this.skeletonObserver0.observe(this.spacerRef);
        this.skeletonObserver0.observe(this.endSpacerRef);
        this.skeletonObserver1.observe(this.spacerRef);
        this.skeletonObserver1.observe(this.endSpacerRef);

        this.visibilityChangeSubscription = DocumentEvents.passive.visibilityChange$.subscribe(
            () => this.onDocumentVisibilityChange()
        );


        // set isRendering as soon as possible
        // eslint-disable-next-line @typescript-eslint/unbound-method
        const origSetAttribute = this.renderIndexRef.setAttribute;
        this.renderIndexRef.setAttribute = (qualifiedName: string, value: string) => {
            // Update pivots just before the render (Blazor sets attributes before changing nodes).
            // Must not throw here — it would break Blazor's render.
            try {
                const time = Date.now();
                debugLog?.log(`renderStartedAt: `, time, value);
                this.updateState('renderIndex.setAttribute', this.state, { renderStartedAt: time });
                // Last pre-mutation moment — the no-jump anchor must be captured here, not inside
                // syncLayoutAfterRender (by then the DOM is already shifted by inserted items but not
                // yet compensated by container.top).
                this.debug?.noteRenderStart(this.captureViewportAnchor());
                origSetAttribute.call(this.renderIndexRef, qualifiedName, value);
                // eslint-disable-next-line @typescript-eslint/no-misused-promises
                fastRaf(() => this.endRender());
            } catch (e) {
                warnLog?.log('renderIndex.setAttribute: failed', e);
            }
        };
        if (this.parseRenderState() === null)
            this.updateState('ctor: initial render', this.state, { renderStartedAt: Date.now() });

        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;
        const mutationRecord: MutationRecord = {
            type: 'childList',
            addedNodes: this.containerRef.childNodes,
            removedNodes: this.endAnchorRef.childNodes,
            attributeName: null,
            attributeNamespace: null,
            nextSibling: null,
            oldValue: null,
            previousSibling: null,
            target: this.containerRef,
        };
        this.onItemSetChange([mutationRecord], this.itemSetChangeObserver);

        InfiniteList._instances.add(this);
        this.skeletonWatchdogTimer = setInterval(() => this.checkSkeletonWatchdog(), SkeletonWatchdogInterval);
        this.startRevealWatch();
        if (InfiniteList.enableDebug)
            this.startDebug();
    };

    /** Called by blazor */
    public dispose() {
        debugLog?.log(`dispose()`);
        this.isDisposed = true;
        this.debug?.stop();
        this.debug = null;
        this.scrollController.dispose();
        InfiniteList._instances.delete(this);
        if (this.skeletonWatchdogTimer !== null) {
            clearInterval(this.skeletonWatchdogTimer);
            this.skeletonWatchdogTimer = null;
        }
        this.abortController.abort();
        this.itemSetChangeObserver.disconnect();
        this.skeletonObserver0.disconnect();
        this.skeletonObserver1.disconnect();
        this.visibilityObserver.disconnect();
        this.sizeObserver.disconnect();
        this.visibilityChangeSubscription.unsubscribe();
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
        this.ref.removeEventListener('scroll', this.onScroll);
        this.ref.removeEventListener('scrollend', this.onScrollEnd);
        this.ref.removeEventListener('pointerdown', this.onPointerDown);
        this.ref.removeEventListener('pointerup', this.onPointerUp);
        this.ref.removeEventListener('pointercancel', this.onPointerUp);
        this.ref.removeEventListener('wheel', this.onWheel);
    }

    /** Called by blazor */
    public reset() {
        debugLog?.log(`reset()`);
        this.isPointerDown = false;
        this.items.clear();
        this.sizeCache.clear();
        this.updateState('reset', this.state, {
            lastViewport: null,
            viewport: null,
            lastQueryTime: null,
            stickyEdge: null,
            query: VirtualListDataQuery.None,
            lastQuery: VirtualListDataQuery.None,
            orderedItems: [],
            pivots: [],
            minStart: null,
            maxEnd: null,
            isStartKnown: false,
            isEndKnown: false,
            renderState: {
                renderIndex: -1,
                query: VirtualListDataQuery.None,
                keyRange: new Range<string>('', ''),
                beforeCount: null,
                afterCount: null,
                estimatedCount: null,
                count: 0,
                hasVeryFirstItem: false,
                hasVeryLastItem: false,
            },
        });
    }

    /** Called by blazor */
    public renderSkipped(): void {
        debugLog?.log(`renderSkipped()`);
        this.updateState('renderSkipped', this.state, { renderStartedAt: null, renderCompletedAt: Date.now() });
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
    }

    public getStateChangeLog(lastN?: number, endStateEvery = 10): Record<string, unknown>[] {
        const history = lastN
            ? this._stateHistory.slice(-lastN)
            : this._stateHistory;

        const log: Record<string, unknown>[] = [];
        for (let i = 0; i < history.length; i++) {
            const s = history[i];
            const endState = i + 1 < history.length ? history[i + 1].state : this._state;

            const change: Record<string, unknown> = {};
            for (const key of s.changedFields)
                change[key] = endState[key];

            const entry: Record<string, unknown> = { reason: s.reason, change };
            if (i % endStateEvery === 0)
                entry.state = endState;
            log.push(entry);
        }
        return log;
    }

    // Private methods

    // The wrapper stays CSS-hidden (c-initially-hidden) until the chain is positioned, so the user never
    // sees the pre-positioned frames. Visual only; polls per frame with a timeout backstop.
    private startRevealWatch(): void {
        const check = () => {
            if (this.isDisposed || this.isContainerRevealed)
                return;
            if (this.isContentPlaced() || Date.now() - this.createdAt > RevealTimeout)
                this.revealContainer();
            else
                requestAnimationFrame(check);
        };
        requestAnimationFrame(check);
    }

    private revealContainer(): void {
        if (this.isContainerRevealed)
            return;
        this.isContainerRevealed = true;
        // Inline style beats the c-initially-hidden class, so later re-renders keeping the class stay visible.
        this.wrapperRef.style.visibility = 'visible';
    }

    // True once the loaded chain sits at its intended target (deep-link key visible / preferred edge flush),
    // i.e. the initial scroll has landed. Read-only DOM probe.
    private isContentPlaced(): boolean {
        const items = this.state.orderedItems;
        const rs = this.state.renderState;
        if (!items?.length) {
            // Confirmed-empty chat (both ends loaded, nothing between): nothing to position, reveal so its
            // placeholder shows. A still-loading chat has neither end known yet, so this stays hidden.
            return rs.hasVeryFirstItem && rs.hasVeryLastItem;
        }
        if (this.hasUnmeasuredItems)
            return false;
        const vr = this.ref.getBoundingClientRect();
        if (vr.height <= 0)
            return false;
        const scrollToKey = this.state.renderState.scrollToKey;
        if (scrollToKey) {
            const el = this.getItemRef(scrollToKey);
            if (!el)
                return false;
            const r = el.getBoundingClientRect();
            return r.height > 0 && r.bottom > vr.top && r.top < vr.bottom;
        }
        if (this.defaultEdge === VirtualListEdge.End) {
            const ea = this.endAnchorRef.getBoundingClientRect();
            return Math.abs(ea.bottom - vr.bottom) <= EdgePlacedEpsilon;
        }
        const first = this.getFirstItemRef();
        return first != null && Math.abs(first.getBoundingClientRect().top - vr.top) <= EdgePlacedEpsilon;
    }

    private startDebug(): void {
        this.debug ??= new VirtualListDebug(this as unknown as VirtualListDebugTarget);
        this.debug.start();
    }

    private updateState(reason: string, prev: VirtualListState, changes: Partial<VirtualListState>): VirtualListState {
        const changedFields: (keyof VirtualListState)[] = [];
        for (const key of Object.keys(changes) as (keyof VirtualListState)[]) {
            if (changes[key] !== prev[key])
                changedFields.push(key);
        }
        if (changedFields.length === 0)
            return prev;

        // Snapshot previous state into ring buffer
        const snapshot: VirtualListStateSnapshot = {
            reason,
            time: Date.now(),
            state: this.snapshotState(prev),
            changedFields,
        };
        if (this._stateHistory.length >= StateHistoryCapacity)
            this._stateHistory.shift();
        this._stateHistory.push(snapshot);

        // Apply changes
        this._state = { ...prev, ...changes };
        this._stateVersion++;

        debugLog?.log('[state]', reason, changedFields, changes);
        this.validateState(reason, prev, this._state, changedFields);
        return this._state;
    }

    private snapshotState(state: VirtualListState): VirtualListState {
        return {
            ...state,
            orderedItems: [...state.orderedItems],
            pivots: [...state.pivots],
        };
    }

    private validateState(
        reason: string,
        prev: VirtualListState,
        next: VirtualListState,
        changedFields: readonly (keyof VirtualListState)[],
    ): void {
        const warnings: string[] = [];

        // itemRange set to non-null while orderedItems is empty
        if (changedFields.includes('itemRange') && next.itemRange != null && next.orderedItems.length === 0)
            warnings.push('itemRange set to non-null while orderedItems is empty');

        // Viewport jumping by more than 2x its size in a single update
        if (changedFields.includes('viewport') && prev.viewport && next.viewport) {
            const vpSize = Math.max(prev.viewport.size, next.viewport.size);
            const jump = Math.abs(next.viewport.start - prev.viewport.start);
            if (vpSize > 0 && jump > vpSize * 2)
                warnings.push(`viewport jumped ${jump}px (${(jump / vpSize).toFixed(1)}x viewport size): `
                    + `[${prev.viewport.start}, ${prev.viewport.end}] -> [${next.viewport.start}, ${next.viewport.end}]`);
        }

        // renderStartedAt set while already set (overlapping renders)
        if (changedFields.includes('renderStartedAt') && prev.renderStartedAt != null && next.renderStartedAt != null
            && prev.renderStartedAt !== next.renderStartedAt)
            warnings.push(`renderStartedAt overwritten: ${prev.renderStartedAt} -> ${next.renderStartedAt} (overlapping render?)`);

        // stickyEdge referencing an itemKey not present in orderedItems
        if (changedFields.includes('stickyEdge') && next.stickyEdge != null && next.orderedItems.length > 0) {
            const hasKey = next.orderedItems.some(i => i.key === next.stickyEdge!.itemKey);
            if (!hasKey)
                warnings.push(`stickyEdge.itemKey="${next.stickyEdge.itemKey}" not found in orderedItems (${next.orderedItems.length} items)`);
        }

        // itemRange jumping significantly after rebuildItemRangeFromAnchor
        if (changedFields.includes('itemRange') && prev.itemRange && next.itemRange) {
            const rangeJump = Math.abs(next.itemRange.start - prev.itemRange.start);
            const rangeSize = Math.max(prev.itemRange.size, next.itemRange.size);
            if (rangeSize > 0 && rangeJump > rangeSize * 2)
                warnings.push(`itemRange jumped ${rangeJump}px (${(rangeJump / rangeSize).toFixed(1)}x range size): `
                    + `[${prev.itemRange.start}, ${prev.itemRange.end}] -> [${next.itemRange.start}, ${next.itemRange.end}]`);
        }

        if (warnings.length > 0) {
            console.warn(
                `[InfiniteList] ⚠ after "${reason}":\n` + warnings.map(w => `  ${w}`).join('\n'),
                this.getStateChangeLog(10));
        }
    }

    private get isRendering(): boolean {
        return !!this.state.renderStartedAt;
    }

    private get isInitialRender(): boolean {
        const now = Date.now();
        // debugLog?.log('scrollToEdge: schedule', edge, useSmoothScroll, reason);
        // first 1.5 seconds after creating the virtual list
        return now - this.createdAt < 1500;
    }

    private get hasUnmeasuredItems(): boolean {
        return this.unmeasuredItems.size > 0 || !this.state.orderedItems;
    }

    private get knownRange(): NumberRange | null {
        return this.state.minStart == null || this.state.maxEnd == null
            ? null
            : new NumberRange(this.state.minStart, this.state.maxEnd);
    }

    private parseRenderState(): VirtualListRenderState | null {
        try {
            const rsJson = this.renderStateRef.textContent;
            if (rsJson == null || rsJson === '')
                return null;

            const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
            if (rs.renderIndex <= this.state.renderState.renderIndex)
                return null;

            const riText = this.renderIndexRef.dataset.renderIndex;
            if (riText == null || riText == '')
                return null;

            const ri = Number.parseInt(riText);
            if (ri != rs.renderIndex)
                return null;

            return rs;
        } catch (e) {
            warnLog?.log('parseRenderState(): failed', e);
            return null;
        }
    }

    private onItemSetChange = (mutations: MutationRecord[], _observer: MutationObserver): void => {
        if (!this.isRendering) {
            if (mutations.length > 0)
                warnLog?.log('onItemSetChange: there are mutations, but isRendering() == false');
            this.updateState('onItemSetChange: not rendering', this.state, { renderStartedAt: Date.now() });
        }
        if (!this.state.renderStartedAt)
            this.updateState('onItemSetChange: renderStartedAt', this.state, { renderStartedAt: Date.now() });
        const startedAt = this.state.renderStartedAt!;
        if (debugLog) {
            const removedCount = mutations.reduce((prev, m) => prev + m.removedNodes.length, 0);
            const addedCount = mutations.reduce((prev, m) => prev + m.addedNodes.length, 0);
            const queryDuration = Math.max(0, startedAt - (this.state.lastQueryTime ?? startedAt));
            debugLog?.log(
                `onItemSetChange: query duration: `, queryDuration,
                '; added: ', addedCount,
                '; removed: ', removedCount,
                '; startedAt: ', startedAt);
        }

        // request recalculation of the item range and order item list as we've got new items
        this.cachedAllItemRefs = null;
        this.updateState('onItemSetChange', this.state, { itemRange: null });

        // process removed nodes first
        const keysToRemove = new Set<string|null>();
        for (const mutation of mutations) {
            if (mutation.type !== 'childList')
                continue;

            for (const node of mutation.removedNodes) {
                const nodeElement = node as HTMLElement;
                const isGroup = nodeElement.classList?.contains('group');
                // TODO(AK): fix eslint error

                if (!nodeElement.dataset && !isGroup)
                    continue;

                const itemRefs = this.getChildItemRefs(nodeElement);
                for (const itemRef of itemRefs) {
                    const key = getItemKey(itemRef);
                    keysToRemove.add(key);
                    this.sizeObserver.unobserve(itemRef);
                    this.visibilityObserver.unobserve(itemRef);
                    itemRef.removeEventListener('touchend', this.onInteractiveEvent);
                    itemRef.removeEventListener('click', this.onInteractiveEvent);
                }
            }
        }

        // process added nodes
        for (const mutation of mutations) {
            if (mutation.type !== 'childList')
                continue;

            for (const node of mutation.addedNodes) {
                const nodeElement = node as HTMLElement;
                const isGroup = nodeElement.classList?.contains('group');
                // TODO(AK): fix eslint error

                if (!nodeElement.dataset && !isGroup)
                    continue;

                if (isGroup) {
                    const groupRefs = this.getChildGroupRefs(nodeElement);
                    for (const groupRef of groupRefs)
                        this.itemSetChangeObserver.observe(groupRef, { childList: true });
                }
                const itemRefs = this.getChildItemRefs(nodeElement);
                for (const itemRef of itemRefs) {
                    const key = getItemKey(itemRef);
                    if (!key)
                        continue;

                    keysToRemove.delete(key);
                    const oldItem = this.items.get(key);
                    const newItem = this.createListItem(key, itemRef);
                    if (oldItem) {
                        // A retained item keeps its range (anchored at start; the end tracks the new size):
                        // retained ranges are the only anchors that survive a render. Pivots are cleared on
                        // every user scroll, so resetting non-pivot ranges here left scroll-triggered renders
                        // with no cornerstone at all — rebuildItemRangeFromAnchor then re-centered the chain
                        // at InfiniteSize/2 and everything on screen jumped by half the loaded chunk.
                        if (oldItem.range && newItem.size && newItem.size > 0)
                            oldItem.range = new NumberRange(oldItem.range.start, oldItem.range.start + newItem.size);
                        oldItem.size = newItem.size;
                        oldItem.shouldSkipKey = newItem.shouldSkipKey;
                        if (oldItem?.size && oldItem.size > 0)
                            this.unmeasuredItems.delete(key);
                    } else
                        this.items.set(key, newItem);
                }
            }
        }

        // remove items that were removed and not added back
        for (const key of keysToRemove) {
            if (!key)
                continue;

            this.items.delete(key);
            this.unmeasuredItems.delete(key);
            this.visibleItems.delete(key);
        }

        this.updateOrderedItems();
        // Call synchronously to prevent 1-frame jumps. Will force reflow
        void this.endRender();
    }

    private onResize = (entries: ResizeObserverEntry[], _observer: ResizeObserver): void => {
        // debugLog?.log('onResize: ', [...entries]);
        let itemsWereMeasured = false;
        let notAnItem = false;
        let viewportResized = false;
        let existingResizedCount = 0;
        let totalExistingSizeDiff = 0;
        let endAnchorHasChanged = false;
        const itemRefsWithWrongSize = new Array<HTMLElement>();
        for (const entry of entries) {
            const key = getItemKey(entry.target as HTMLElement);
            const rowGap = this.rowGap;
            // entry.contentRect is always content-box even under box:'border-box', undersizing items whose
            // spacing uses padding (drifts the chain math). Prefer borderBoxSize, fall back to contentRect.
            const heightPx = entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height;
            const size = Math.ceil(heightPx + rowGap);
            if (!key) {
                notAnItem = true;
                if (entry.target === this.endAnchorRef) {
                    this.updateState('onResize: endAnchor', this.state, { endAnchorSize: size });
                    endAnchorHasChanged = true;
                }
                else if (entry.target === this.ref)
                    viewportResized = true;
                continue; // container or footer also can be resized
            }

            const item = this.items.get(key);
            if (item) {
                const itemRef = entry.target as HTMLElement;
                if (size == 0 && !isLaidOut(itemRef))
                    itemRefsWithWrongSize.push(itemRef);
                else {
                    this.debug?.noteItemMeasure(key, size, item.createdAt, itemRef);
                    const hasRemoved = this.unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                    // >= 0, not > 0: a measured size of zero is a real one (an item that is laid out and
                    // renders nothing), so the reciprocal 0 -> positive change has to invalidate the range
                    // too. Under `oldSize > 0` it didn't, and only this item's own range was updated -
                    // every following range and the aggregate itemRange stayed short by the difference.
                    // -1 is the unmeasured sentinel and still doesn't count as a resize.
                    const oldSize = item.size;
                    if (oldSize != null && oldSize >= 0 && size != oldSize) {
                        existingResizedCount++;
                        itemsWereMeasured = true;
                        totalExistingSizeDiff += size - oldSize;
                    }
                    item.size = size;
                    // Keep the range anchored at start (same reason as in onItemSetChange): a resize must
                    // not cost the item its anchor role, or a resize burst with cleared pivots re-centers
                    // the whole chain.
                    if (item.range)
                        item.range = new NumberRange(item.range.start, item.range.start + size);

                    this.sizeCache.set(key, size);
                    if (size > 0)
                        this.statistics.addItem(size);
                }
            } else {
                const hasRemoved = this.unmeasuredItems.delete(key);
                itemsWereMeasured ||= hasRemoved;
            }
        }
        if (itemRefsWithWrongSize.length) {
            // ensure we have all sizes calculated
            itemRefsWithWrongSize.forEach((itemRef) => {
                const key = getItemKey(itemRef);
                if (key)
                    this.unmeasuredItems.add(key);
            });
            fastRaf(() => this.measureItems());
        }
        if (notAnItem) {
            this.updateState('onResize: windowScrollTop', this.state, { windowScrollTop: window.visualViewport?.offsetTop ?? window.scrollY });
            // Re-pin (don't recreate) the sticky edge on resize. 'viewport-resize' is honored even on the
            // initial render, so panels/keyboard resizing right after open still keep the edge pinned.
            // endAnchorHasChanged too: when the end-anchor headroom grows (e.g. the audio panel appears,
            // h-12 -> h-20) alongside a last-item resize, the End max shifts by that delta - without re-pinning
            // here the pin keeps the stale max and leaves that delta as dead space below the last block.
            if (this.state.stickyEdge?.edge === this.defaultEdge && (viewportResized || endAnchorHasChanged || !itemsWereMeasured))
                this.scrollToEdge(this.defaultEdge, false, viewportResized ? 'viewport-resize' : 'non-item-resize');

            if (DeviceInfo.isIos) {
                const htmlElement = document.getElementsByTagName('html')[0];
                const bodyElement = document.body;
                if (this.state.windowScrollTop == 0) {
                    htmlElement.style.position = 'static';
                    htmlElement.style.overflowX = null!;
                    bodyElement.style.position = 'static';
                    bodyElement.style.overflowX = null!;
                } else {
                    // Hack for iOS to keep text editor visible when virtual keyboard appears or new message is submitted
                    htmlElement.style.position = 'fixed';
                    htmlElement.style.overflowX = 'hidden';
                    bodyElement.style.position = 'fixed';
                    bodyElement.style.overflowX = 'hidden';
                }
            }
        }

        // recalculate item range as some elements were updated
        if (itemsWereMeasured || existingResizedCount > 0 || endAnchorHasChanged) {
            this.updateState('onResize: measured', this.state, { itemRange: null });

            const now = Date.now();
            // A render in flight will re-pin via endRender().
            if (this.state.renderStartedAt)
                return;

            // Scroll restoration is skipped here, but the recompute shifted itemRange — re-pin the
            // container (no scroll) so the DOM doesn't drift from the model.
            const skipScrollRestore =
                (this.state.renderState.scrollToKey != null && now - this.state.lastProgrammaticScrollAt < ScrollDebounce)
                || now - this.state.renderCompletedAt < ScrollDebounce;
            if (skipScrollRestore) {
                this.ensureItemRangeCalculated();
                this.repinContainerToModel();
                // Still follow edge-item growth while pinned: a new message stamps renderCompletedAt,
                // so transcript growth of the latest messages lands in this window and would slip below.
                if (this.state.stickyEdge?.edge === this.defaultEdge)
                    this.scrollToEdge(this.defaultEdge, true, 'item-resize');
                return;
            }

            const renderState = { ...this.state.renderState, scrollToKey: undefined };
            const scrollIntent = this.getScrollIntent(renderState);

            // Safe to call syncLayoutAfterRender() directly (no fastRaf): layout is recalculated and we're
            // not mid-render, so no new item-size info can arrive before the next paint.
            void this.syncLayoutAfterRender(renderState, scrollIntent, false);
        }
    };

    private onItemVisibilityChange = (entries: IntersectionObserverEntry[], _observer: IntersectionObserver): void => {
        if (this.isRendering)
            return;

        let hasChanged = false;
        const rs = this.state.renderState;
        const lastItemKey = this.getLastItemKey();
        const firstItemKey = this.getFirstItemKey();
        for (const entry of entries) {
            const itemRef = entry.target as HTMLElement;
            const key = getItemKey(itemRef);
            if (!key) {
                if (this.endAnchorRef === itemRef) {
                    if (entry.isIntersecting) {
                        this.turnOnIsEndAnchorVisibleDebounced();
                        this.turnOffIsEndAnchorVisibleDebounced.reset();
                    } else if (this.state.isEndAnchorVisible) {
                        this.turnOffIsEndAnchorVisibleDebounced();
                        this.turnOnIsEndAnchorVisibleDebounced.reset();
                    }
                }
                continue;
            }
            const item = this.items.get(key);
            if (item?.shouldSkipKey) {
                hasChanged ||= this.visibleItems.has(key);
                this.visibleItems.delete(key);
            }
            else if (!entry.isIntersecting) {
                hasChanged ||= this.visibleItems.has(key);
                this.visibleItems.delete(key);
            } else if ((entry.intersectionRatio >= 0.4 || entry.intersectionRect.height > MinViewPortSize / 2) && entry.isIntersecting) {
                hasChanged ||= !this.visibleItems.has(key);
                this.visibleItems.add(key);
            } else if (key === lastItemKey && entry.isIntersecting && rs.hasVeryLastItem && this.state.isEndAnchorVisible) {
                // the last item is bigger than viewport, but we see the end anchor - so let's mark it visible
                hasChanged ||= !this.visibleItems.has(key);
                this.visibleItems.add(key);
            }
        }
        if (hasChanged) {
            // Both edges are sticky; the preferred edge (defaultEdge) wins when both are visible.
            const lastVisible = rs.hasVeryLastItem && !!lastItemKey && this.visibleItems.has(lastItemKey);
            const firstVisible = rs.hasVeryFirstItem && !!firstItemKey && this.visibleItems.has(firstItemKey);
            // isAtEndEdge() gate: "the last item is visible" is far weaker than "we are at the end" when
            // that item is taller than the viewport - a conversation block at the chat end is exactly
            // that. Without it a fling away re-pinned here right after onScroll dropped the pin, and the
            // next render re-scrolled to the newest.
            if (lastVisible && (this.defaultEdge === VirtualListEdge.End || !firstVisible) && this.isAtEndEdge())
                this.setStickyEdge({ itemKey: lastItemKey, edge: VirtualListEdge.End });
            else if (firstVisible)
                this.setStickyEdge({ itemKey: firstItemKey, edge: VirtualListEdge.Start });

            this.updateVisibleKeysThrottled();
        }
    };

    private onInteractiveEvent = (event: Event): void => {
        const itemRef = event.currentTarget as HTMLElement;
        let key = getItemKey(itemRef);
        if (!key)
            return;

        // Only controls opted in via data-vl-hold arm an interactive pivot; plain taps (play, links,
        // text selection) must not affect anchoring or stickiness. 'always' (expand / Show-more) holds
        // the item and leaves the End edge - a deliberate "read history" action; 'keep-edge' (collapse)
        // holds only when not pinned - when pinned the sticky re-pin absorbs the shrink instead.
        const target = event.target as HTMLElement | null;
        const holdRef = target?.closest<HTMLElement>('[data-vl-hold]');
        if (!holdRef || !itemRef.contains(holdRef))
            return;

        const isPinned = this.state.stickyEdge != null;
        if (holdRef.dataset.vlHold === 'keep-edge' && isPinned)
            return;

        if (isPinned)
            this.setStickyEdge(null);

        // A control marked data-anchor="below" (the live block's Show-more pill) reveals rows ABOVE
        // itself. Hold the first item BELOW this one as the interactive pivot instead of this item, so
        // the revealed rows grow upward while everything from the control down keeps its screen position.
        if (target?.closest('[data-anchor="below"]')) {
            const belowKey = this.getFirstItemKeyBelow(itemRef);
            if (belowKey)
                key = belowKey;
        }

        if (BrowserInfo.appKind === 'Wasm')
            this.updateCurrentPivots(key); // Required to do it synchronously at WASM
        else
            this.scheduleUpdateCurrentPivots(key);
    };

    private onSkeletonVisibilityChange = (
        entries: IntersectionObserverEntry[],
        _observer: IntersectionObserver): void => {
        let isNearSkeleton = false;
        for (const entry of entries) {
            isNearSkeleton ||= entry.isIntersecting
                && entry.boundingClientRect.height > EdgeEpsilon;
        }
        if (isNearSkeleton) {
            this.updateState('skeleton: near', this.state, { isNearSkeleton });
            // reset turn off attempt
            this.turnOffIsNearSkeletonDebounced.reset();
            // this.updateViewportThrottled();
        } else
            this.turnOffIsNearSkeletonDebounced();
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce);

    private turnOffIsNearSkeleton(): void {
        this.updateState('skeleton: off', this.state, { isNearSkeleton: false });
    }

    private checkSkeletonWatchdog(): void {
        if (this.isDisposed)
            return;

        // Check DOM: are spacers actually visible on screen with non-trivial height?
        const viewRect = this.ref.getBoundingClientRect();
        const startRect = this.spacerRef.getBoundingClientRect();
        const endRect = this.endSpacerRef.getBoundingClientRect();
        const startVisible = startRect.height > VisibilityEpsilon
            && startRect.bottom > viewRect.top && startRect.top < viewRect.bottom;
        const endVisible = endRect.height > VisibilityEpsilon
            && endRect.bottom > viewRect.top && endRect.top < viewRect.bottom;

        if (!startVisible && !endVisible) {
            this.skeletonWatchdogLastVersion = -1;
            return;
        }

        // Skeletons visible in DOM — check if state is unchanged since last check
        const version = this._stateVersion;
        if (this.skeletonWatchdogLastVersion !== version) {
            // First time seeing skeletons at this state version — remember and wait
            this.skeletonWatchdogLastVersion = version;
            return;
        }

        // Same state version twice in a row with visible skeletons — report
        this.skeletonWatchdogLastVersion = -1; // reset so we don't spam
        const msg = `[InfiniteList:${this.identity}] ⚠ skeleton watchdog: spacers visible on screen for 2 checks`
            + ` (stateVersion=${version})`
            + `\n  startSpacer: h=${startRect.height.toFixed(0)} visible=${startVisible}`
            + `\n  endSpacer: h=${endRect.height.toFixed(0)} visible=${endVisible}`
            + `\n  viewport: [${viewRect.top.toFixed(0)}, ${viewRect.bottom.toFixed(0)}]`
            + `\n  scrollTop: ${this.ref.scrollTop}`
            + `\n  isRendering: ${this.isRendering}`;
        if (InfiniteList.enableWatchdogFixes) {
            console.warn(msg + '\n  → requesting data', this.getStateChangeLog(10));
            void this.requestData();
        } else {
            console.warn(msg, this.getStateChangeLog(10));
        }
    }

    private turnOffIsEndAnchorVisibleDebounced = debounce(() => this.turnOffIsEndAnchorVisible(), ScrollDebounce);

    private turnOffIsEndAnchorVisible(): void {
        // Double-check DOM — IntersectionObserver can give false negatives during resize
        if (this.isItemPartiallyVisible(this.endAnchorRef)) {
            this.turnOnIsEndAnchorVisibleDebounced();
            return;
        }
        this.updateState('endAnchor: off', this.state, { isEndAnchorVisible: false });
        if (this.state.stickyEdge?.edge === VirtualListEdge.End) {
            const timeSinceProgrammaticScroll = Date.now() - this.state.lastProgrammaticScrollAt;
            if (timeSinceProgrammaticScroll > ScrollDebounce)
                this.setStickyEdge(null);
            else
                this.turnOffIsEndAnchorVisibleDebounced();
        }

        this.updateVisibleKeysThrottled();
    }

    private turnOnIsEndAnchorVisibleDebounced = debounce(() => this.turnOnIsEndAnchorVisible(), ScrollDebounce);

    private async turnOnIsEndAnchorVisible(): Promise<void> {
        // double-check visibility to prevent issues with scroll-to-the-last-item button
        await fastReadRafAsync();

        const viewRect = this.ref.getBoundingClientRect();
        const endSpacerRect = this.endSpacerRef.getBoundingClientRect();
        const isEndAnchorRefVisible = this.isItemPartiallyVisible(this.endAnchorRef, viewRect);
        const isEndSpacerRefVisible = this.isRectPartiallyVisible(endSpacerRect, viewRect)
            && endSpacerRect.height > VisibilityEpsilon;
        const isEndAnchorVisible = isEndAnchorRefVisible && !isEndSpacerRefVisible;
        if (!isEndAnchorVisible) {
            this.updateState('endAnchor: not visible', this.state, { isEndAnchorVisible: false });
            return;
        }

        this.updateState('endAnchor: on', this.state, { isEndAnchorVisible: true });
        if (this.state.renderState.hasVeryLastItem) {
            // Both edges visible (content shorter than viewport) → defaultEdge wins, mirroring updateVisibleItems.
            // Without this a Start-edge list (media/files/links) gets pinned to End the moment its end anchor
            // scrolls in, dropping a single item or the empty placeholder to the bottom instead of the top.
            const firstRef = this.getFirstItemRef();
            const firstVisible = firstRef != null && this.isItemPartiallyVisible(firstRef, viewRect);
            if (this.defaultEdge === VirtualListEdge.End || !firstVisible) {
                const edgeKey = this.getLastItemKey()!;
                this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.End });
            }
        }
        this.updateVisibleKeysThrottled();
    }

    private async endRender(): Promise<void> {
        if (!this.isRendering) {
            debugLog?.log('endRender: not rendering');
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;
            return;
        }
        if (InfiniteList.debugRenderDelayMs > 0)
            await delayMs(Math.random() * InfiniteList.debugRenderDelayMs);
        const rs = this.parseRenderState();
        if (rs === null) {
            this.updateState('endRender: no rs', this.state, { renderStartedAt: null });
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;
            return;
        }

        if (rs.query.isNone) {
            // Reset query - it become irrelevant after render without query
            this.updateState('endRender: reset queries', this.state, { query: VirtualListDataQuery.None, lastQuery: VirtualListDataQuery.None });
        }

        this.updateState('endRender: renderState', this.state, { renderState: rs });

        const startedAt = this.state.renderStartedAt ?? Date.now();
        const now = Date.now();
        debugLog?.log(`endRender, renderIndex = #${rs.renderIndex}, duration = ${now - startedAt}ms, rs =`, rs);

        try {
            // Update statistics
            if (!rs.query.isNone && rs.query.expectedCount)
                this.statistics.addResponse(rs.count, rs.query.expectedCount);

            const scrollIntent = this.getScrollIntent(rs);

            // endRender is already being called from fastRaf, so useRaf = false
            await this.syncLayoutAfterRender(rs, scrollIntent, false);
        } finally {
            this.updateState('endRender: finalize', this.state, {
                renderStartedAt: null,
                renderCompletedAt: Date.now(),
                query: VirtualListDataQuery.None,
                lastViewport: this.state.viewport,
            });
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;
        }

        // Schedule viewport update AFTER finalize (isRendering now false) — the call inside
        // syncLayoutAfterRender can be swallowed by the leading-edge throttle while it was still true.
        this.updateViewportThrottled();
        this.repinIfViewportStrandedDebounced();
        this.debug?.onEvent('render');
    }

    private getScrollIntent(rs: VirtualListRenderState): ScrollIntent {
        const scrollToItemRef = this.getItemRef(rs.scrollToKey);
        let shouldUseSmoothScroll = false;
        let reason: ScrollToEdgeReason = 'unknown';
        let scrollFunc: (() => void) | undefined = undefined;
        // An interactive pivot means the user just clicked an item (expand/collapse a conversation, tap a
        // row): its cornerstone must hold that item's screen position across the render it triggers. A
        // sticky-edge re-pin would win over it — reason 'sticky-edge' disables the interactive layout anchor
        // in syncLayoutAfterRender — and drag the clicked header toward the edge. So the sticky-edge re-pin
        // is limited to non-interactive renders. Any user scroll clears pivots, so a genuine scroll-triggered
        // load-above render carries no interactive pivot and still re-pins the End edge as before.
        const hasInteractivePivot = this.getFreshInteractivePivot() != null;

        if (scrollToItemRef != null) {
            // Server-side scroll request
            const isScrollToKeyVisible = this.isKeyVisible(rs.scrollToKey);
            if (!isScrollToKeyVisible) {
                if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    reason = 'last-item';
                    shouldUseSmoothScroll = this.state.stickyEdge?.edge == VirtualListEdge.End;
                    scrollFunc = () => {
                        this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, reason);
                        this.setStickyEdge({ itemKey: rs.scrollToKey!, edge: VirtualListEdge.End });
                    };
                } else {
                    const blockPosition: ScrollLogicalPosition = rs.scrollToKeyInTheMiddle
                        ? 'center'
                        : 'end'
                    reason = 'item';
                    scrollFunc = () => {
                        // A server nav to a non-end message must release the End sticky-edge; otherwise the
                        // sticky-edge re-pin (here and in onResize) snaps the view straight back to the bottom.
                        if (this.state.stickyEdge?.edge === VirtualListEdge.End)
                            this.setStickyEdge(null);
                        this.scrollTo(scrollToItemRef, false, blockPosition);
                    };
                }
            } else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                shouldUseSmoothScroll = true;
                reason = 'last-item';
                scrollFunc = () => {
                    this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, reason);
                    this.setStickyEdge({ itemKey: rs.scrollToKey!, edge: VirtualListEdge.End });
                };
            }
            else if (!rs.scrollToKeyInTheMiddle) {
                // Keep position of visible item
                scrollFunc = () => this.scrollTo(scrollToItemRef, false, 'end');
            }
        } else if (!hasInteractivePivot
            && this.state.stickyEdge != null
            && (this.state.query.isNone
                || (this.state.stickyEdge.edge === VirtualListEdge.End && this.state.isEndAnchorVisible))) {
            // The End case also covers query renders: a load-above render used to have no scroll intent
            // at all, so any layout shift it caused stranded an at-the-bottom view above the newest item
            // with nothing to re-pin it. Gated on isEndAnchorVisible so it can't fight a scroll away.
            const itemKey = this.state.stickyEdge.edge === VirtualListEdge.Start && rs.hasVeryFirstItem
                ? this.getFirstItemKey()
                : this.state.stickyEdge.edge === VirtualListEdge.End && rs.hasVeryLastItem
                    ? this.getLastItemKey()
                    : null;
            if (itemKey) {
                shouldUseSmoothScroll = itemKey !== this.state.stickyEdge.itemKey;
                reason = 'sticky-edge';
                scrollFunc = () => {
                    this.setStickyEdge({ itemKey, edge: this.state.stickyEdge!.edge });
                    this.scrollToEdge(this.state.stickyEdge!.edge, shouldUseSmoothScroll, reason);
                };
            } else {
                const isEdgeGenuinelyGone = this.state.stickyEdge.edge === VirtualListEdge.End
                    ? !rs.hasVeryLastItem
                    : !rs.hasVeryFirstItem;
                if (isEdgeGenuinelyGone) {
                    this.setStickyEdge(null);
                } else {
                    // Transient: hasVeryLastItem but getLastItemKey() returned null during render.
                    // Keep stickyEdge — scroll to old item if possible.
                    if (this.state.stickyEdge.edge === VirtualListEdge.End) {
                        const itemRef = this.getItemRef(this.state.stickyEdge.itemKey);
                        if (itemRef)
                            scrollFunc = () => this.scrollTo(itemRef, false);
                    }
                }
            }
        } else {
            if (rs.query.isNone && rs.renderIndex === 0) {
                reason = 'no-pivot';
                const edge = this.defaultEdge;
                scrollFunc = () => {
                    this.scrollToEdge(edge, false, reason);
                    // Pin sticky now instead of via the racy end-anchor IntersectionObserver, which
                    // sometimes never fires — leaving the list at the edge but unpinned.
                    const stickyKey = edge === VirtualListEdge.End
                        ? (rs.hasVeryLastItem ? this.getLastItemKey() : null)
                        : (rs.hasVeryFirstItem ? this.getFirstItemKey() : null);
                    if (stickyKey)
                        this.setStickyEdge({ itemKey: stickyKey, edge });
                };
            }
        }

        return { shouldUseSmoothScroll: shouldUseSmoothScroll, reason, scroll: scrollFunc };
    }

    private readonly updateViewportThrottled = throttle(
        () => this.updateViewport(true),
        UpdateViewportInterval,
        'default');

    private async updateViewport(isThrottled = false): Promise<void> {
        const rs = this.state.renderState;
        if (this.isDisposed || this.isRendering)
            return;

        // if (rs.renderIndex > 0)
        //     return; // Debug helper

        // do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        const hasScheduled = await fastReadRafAsync(`updateViewport_${this.identity}`);
        if (!hasScheduled)
            return; // unable to schedule requestAnimationFrame, same key has already been scheduled

        const viewport = this.calculateViewport();
        if (viewport == null)
            return;

        this.updateState('updateViewport', this.state, { viewport });
        await this.requestData();
    }

    private calculateViewport(): NumberRange | null {
        const viewportHeight = this.ref.clientHeight;
        const scrollTop = this.ref.scrollTop;
        if (viewportHeight === 0 && scrollTop === 0)
            return null; // Unable to calculate viewport as the element is hidden

        const viewport = new NumberRange(scrollTop, scrollTop + viewportHeight);

        const oldViewport = this.state.viewport ?? this.state.lastViewport;
        if (oldViewport && viewport) {
            const scrollDirection = viewport.start < oldViewport.start ? 'up' : 'down';
            this.updateState('calcViewport: ' + scrollDirection, this.state, { scrollDirection });
        }
        return viewport;
    }

    private readonly updateVisibleKeysThrottled = throttle(
        () => this.updateVisibleKeys(),
        UpdateItemVisibilityInterval,
        'delayHead');
    private async updateVisibleKeys(): Promise<void> {
        if (this.isDisposed || !this.state.renderState.keyRange.start)
            return;

        const visibleItems = [...this.visibleItems].sort((a, b) => this.keySortCollator.compare(a, b));
        // "Nothing is visible" is only true of a settled list. A fast fling routinely outruns rendering
        // and empties the viewport for a few frames, and reporting that makes the app act on a position
        // the user is not at: the chat view reads an empty viewport as a tab resume and scrolls to the
        // first unread, which - right after "scroll to the newest" - is the newest, so the fling snaps
        // back to the bottom. turnOffIsScrolling re-reports once the list settles, so nothing is lost.
        if (visibleItems.length === 0 && this.state.isScrolling)
            return;

        const isEndAnchorVisible = this.state.isEndAnchorVisible;
        // debugLog?.log(`updateVisibleKeys: calling UpdateItemVisibility:`, visibleItems, isEndAnchorVisible);
        try {
            await this.blazorRef.invokeMethodAsync(
                'UpdateItemVisibility',
                this.identity,
                visibleItems,
                isEndAnchorVisible);
        } catch (e) {
            // InfiniteList.DisposeAsync disposes BlazorRef right after the JS dispose(), so a call that
            // passed the isDisposed check above can still land on a disposed DotNetObjectReference -
            // the check can't span the await. Left unhandled, that rejection trips the error barrier.
            warnLog?.log('updateVisibleKeys: failed', e);
        }
    }

    private updateOrderedItems(): void {
        const orderedItems = new Array<VirtualListItem>();
        // store item order
        for (const itemRef of this.getAllItemRefs()) {
            const key = getItemKey(itemRef);
            if (!key)
                continue;

            const item = this.items.get(key);
            if (item) {
                orderedItems.push(item);
            } else {
                const newItem = this.createListItem(key, itemRef);
                this.items.set(key, newItem);
                orderedItems.push(newItem);
            }
        }
        this.updateState('updateOrderedItems', this.state, { orderedItems });
    }

    private createListItem(itemKey: string, itemRef: HTMLElement): VirtualListItem {
        const newItem = new VirtualListItem(itemKey);
        const size = this.sizeCache.get(itemKey);
        if (size && size > 0)
            newItem.size = size;
        else
            this.unmeasuredItems.add(itemKey);
        this.sizeObserver.observe(itemRef, { box: 'border-box' });
        this.visibilityObserver.observe(itemRef);
        itemRef.addEventListener('touchend', this.onInteractiveEvent, { passive: true });
        itemRef.addEventListener('click', this.onInteractiveEvent, { passive: true });
        // Blazor renders a true bool attribute as a valueless boolean attribute (data-skip=""), not
        // data-skip="true"; treat any present, non-"false" value as skip so the flag isn't silently lost.
        newItem.shouldSkipKey = itemRef.dataset.skip != null && itemRef.dataset.skip !== 'false';
        return newItem;
    }

    // Event handlers

    private onScroll = (ev: Event): void => {
        // Sampled first and on EVERY scroll event, including the ones the guards below drop: the
        // sticky-edge direction test needs the immediately preceding position. state.viewport looks like
        // it would do, but it's a 64ms-throttled sample, so mid-fling it can sit on the far side of a
        // re-pin and invert the comparison.
        const scrollTop = this.ref.scrollTop;
        const prevScrollTop = this.lastObservedScrollTop;
        this.lastObservedScrollTop = scrollTop;

        this.updateState('onScroll', this.state, { isScrolling: true });
        this.turnOffIsScrollingDebounced();

        if (this.isRendering)
            return;

        if (!ev.isTrusted)
            return; // Ignore non-user initiated scrolls

        // Ignore the trusted scroll event that setting scrollTop in syncLayoutAfterRender fires —
        // otherwise it's misread as a user scroll, clearing pivots and causing a visual jump.
        if (Date.now() - this.state.lastProgrammaticScrollAt < ProgrammaticScrollGuardMs)
            return;

        // Drop the sticky edge once the user moves off it, so it stops auto-following. Not gated on
        // isPointerDown: iOS hands the gesture to the native scroller and cancels pointer events when a
        // fling starts, so through the whole momentum phase the pin survived and the re-pins dragged the
        // view back. Direction-gated instead, so a re-pin - which moves toward the edge - keeps its pin
        // and the list still follows new messages.
        const stickyEdge = this.state.stickyEdge;
        if (stickyEdge != null && !this.isAtStickyEdge()) {
            const isAwayFromEdge = prevScrollTop != null && scrollTop !== prevScrollTop
                && (stickyEdge.edge === VirtualListEdge.End
                    ? scrollTop < prevScrollTop
                    : scrollTop > prevScrollTop);
            if (isAwayFromEdge || this.isPointerDown)
                this.setStickyEdge(null);
        }

        // Detect user scroll direction on the first trusted scroll event
        if (this.userScrollDirection === 'none') {
            const scrollTop = this.ref.scrollTop;
            const prevViewport = this.state.viewport ?? this.state.lastViewport;
            if (prevViewport) {
                const prevScrollTop = prevViewport.start;
                if (scrollTop !== prevScrollTop) {
                    this.userScrollDirection = scrollTop < prevScrollTop ? 'up' : 'down';
                    warnLog?.log(`User scroll: +${this.userScrollDirection} ${this.renderedWindowInfo()}`);
                }
            }
        }

        // Reset pivots on scroll (skip when already empty — updateState clones all items, costly per frame)
        if (this.state.pivots.length > 0)
            this.updateState('onScroll: clearPivots', this.state, { pivots: [] });
        this.updateViewportThrottled();
    };

    private onScrollEnd = (): void => {
        this.turnOffIsScrolling();
    }

    private onPointerDown = (): void => {
        this.isPointerDown = true;
    };

    private onPointerUp = (): void => {
        this.isPointerDown = false;
    };

    private onWheel = (ev: WheelEvent): void => {
        // Mobile inertial/momentum scrolling fires wheel events — ignore them
        if (DeviceInfo.isMobile)
            return;

        const { stickyEdge } = this.state;
        if (stickyEdge != null && !this.isAtStickyEdge()) {
            const isAwayFromEdge = stickyEdge.edge === VirtualListEdge.End
                ? ev.deltaY < 0
                : ev.deltaY > 0;
            if (isAwayFromEdge)
                this.setStickyEdge(null);
        }
    };

    private onDocumentVisibilityChange(): void {
        if (document.hidden) {
            debugLog?.log(`onDocumentVisibilityChange: hidden, clearing stickyEdge`);
            this.turnOffIsEndAnchorVisible()
            this.turnOnIsEndAnchorVisibleDebounced.reset();
            this.turnOffIsEndAnchorVisibleDebounced.reset();
        } else {
            debugLog?.log(`onDocumentVisibilityChange: visible, re-checking endAnchor visibility`);
            this.turnOnIsEndAnchorVisibleDebounced();
        }
    }

    private scheduleUpdateCurrentPivots(interactiveKey?: string): void {
        if (this.isDisposed)
            return;

        fastRaf(() => this.updateCurrentPivots(interactiveKey));
    }

    private updateCurrentPivots(interactiveKey?: string, force = false): void {
        // `force` lets syncLayoutAfterRender() capture pivots mid-render (isRendering true) when the DOM
        // is already laid out — needed to anchor the coordinate system.
        if (!force && this.isRendering)
            return;
        if (this.state.isUpdatingPivots)
            return;

        try {
            this.updateState('updatePivots: start', this.state, { isUpdatingPivots: true });

            const time = Date.now();
            const pivots = new Array<Pivot>();
            const pivotRefs = new Array<HTMLElement>();
            // add query edges and second\last items as pivots

            // do not use first item as pivot - it might be changed during rendering of items above - e.g. author circle might disappear

            let medianVisibleKey = '';
            if (this.visibleItems.size) {
                const visibleItems = [...this.visibleItems.values()];
                medianVisibleKey = visibleItems[Math.floor(visibleItems.length / 2)];
            }

            const viewRect = this.ref.getBoundingClientRect();

            const itemKeys: string[] = [interactiveKey ?? '', medianVisibleKey, this.state.query.keyRange?.end, this.state.query.keyRange?.start];
            for (const itemKey of itemKeys) {
                if (!itemKey)
                    continue;

                const item = this.items.get(itemKey);
                const pivotRef = this.getItemRef(itemKey);
                const isInteractive = itemKey === interactiveKey;
                if (!pivotRef || !item || (!isInteractive && item.shouldSkipKey) || !item.range)
                    continue;

                pivotRefs.push(pivotRef);
                // measure scroll position
                let stickyOffset: number | null = null;
                const itemRect = pivotRef.getBoundingClientRect();
                const isVisible = this.isRectIntersects(itemRect, viewRect);
                if (isInteractive) {
                    const isSticky = window.getComputedStyle(pivotRef).position === 'sticky';
                    if (isSticky) {
                        // adjust range to the desired sticky position
                        const staticOffset = getOriginalPosition(pivotRef);
                        const actualOffset = pivotRef.getBoundingClientRect().top;
                        stickyOffset = actualOffset - staticOffset;
                    }
                }
                const pivot: Pivot = {
                    itemKey,
                    range: item.range,
                    time,
                    isVisible,
                    isInteractive,
                    stickyOffset,
                };
                pivots.push(pivot);
            }
            this.updateState('updatePivots', this.state, { pivots });
        }
        finally {
            this.updateState('updatePivots: end', this.state, { isUpdatingPivots: false });
            // if (interactiveKey)
            //     void this.syncLayoutAfterRender(this.state.renderState);
        }
    }

    // A stale interactive pivot must not hijack later unrelated renders (it used to silently kill the
    // sticky edge), so interactive semantics apply only within a short window after the click.
    private getFreshInteractivePivot(): Pivot | null {
        const pivot = this.state.pivots.find(p => p.isInteractive);
        return pivot != null && Date.now() - pivot.time <= InteractivePivotTtlMs ? pivot : null;
    }

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce);

    private turnOffIsScrolling() {
        if (this.userScrollDirection !== 'none') {
            warnLog?.log(`User scroll: -${this.userScrollDirection} ${this.renderedWindowInfo()}`);
            this.userScrollDirection = 'none';
        }
        this.updateState('turnOffIsScrolling', this.state, { isScrolling: false, scrollDirection: 'none' as const });

        // this line below can fix rendering artifacts when some entries are blank
        // but adds significant stutter during scroll
        // this.forceRepaintThrottled();

        if (this.isRendering || this.isDisposed)
            return;

        const turnOffScrollingCallback = this.turnOffScrollingCallback;
        if (turnOffScrollingCallback) {
            this.turnOffScrollingCallback = undefined;
            turnOffScrollingCallback();
        }

        void this.updateViewport();
        this.updateVisibleKeysThrottled();
        this.repinIfViewportStrandedDebounced();
    }

    private renderedWindowInfo(): string {
        const { orderedItems, viewport, itemRange } = this.state;
        const count = orderedItems.length;
        if (!viewport || !itemRange || viewport.size <= 0)
            return `items=${count}`;

        const vp = viewport.size;
        const toVp = (px: number): string => (px / vp).toFixed(1);
        const above = viewport.start - itemRange.start;
        const below = itemRange.end - viewport.end;
        return `items=${count} loaded=${toVp(itemRange.size)}vp ↑${toVp(above)}vp ↓${toVp(below)}vp`;
    }

    private repinIfViewportStrandedDebounced = debounce(() => this.repinIfViewportStranded(), ScrollDebounce);

    // Safety net: if the viewport ends up catastrophically far from the chain — the no-pivot/recenter
    // "blank" (~InfiniteSize/2 gap) that no sticky edge or clamp recovers — snap back to the default edge.
    // Gated by StrandedGapThreshold so it never fires for normal scroll/overscroll/skeleton gaps.
    private repinIfViewportStranded(): void {
        if (this.isRendering || this.isDisposed)
            return;
        if (this.state.isScrolling)
            return; // a scroll is in flight — not stuck

        const clientHeight = this.ref.clientHeight;
        if (clientHeight <= 0)
            return;

        const scrollTop = this.ref.scrollTop;
        const containerTop = parseFloat(this.containerRef.style.top) || 0;
        const containerBottom = containerTop + this.containerRef.offsetHeight;
        const gap = scrollTop + clientHeight < containerTop
            ? containerTop - (scrollTop + clientHeight)
            : scrollTop > containerBottom
                ? scrollTop - containerBottom
                : 0;
        if (gap < StrandedGapThreshold)
            return; // viewport overlaps, or is only normally far from, the rendered container

        debugLog?.log('repinIfViewportStranded: re-pinning to edge', this.defaultEdge,
            { scrollTop, clientHeight, containerTop, containerBottom, gap });
        this.scrollToEdge(this.defaultEdge, false, 'stranded');
    }

    // Debug-only: a keyed content item to anchor the no-jump check on. With a key, returns that item's
    // viewport-relative top (or null if gone); without, the keyed item nearest the viewport centre.
    private captureViewportAnchor(key?: string): { key: string; top: number } | null {
        const viewRect = this.ref.getBoundingClientRect();
        if (key != null) {
            const li = this.containerRef.querySelector<HTMLElement>(`.item[data-key="${CSS.escape(key)}"]`);
            if (li == null)
                return null;
            const r = li.getBoundingClientRect();
            return r.height > 0 ? { key, top: r.top - viewRect.top } : null;
        }
        // Prefer the interactive pivot's item: an interactive render (expand/collapse a conversation, tap a
        // row) intentionally holds THAT item's position while items below it shift with the content change,
        // so anchoring on a below item would false-flag their legitimate move. Non-interactive renders fall
        // back to the viewport-centre item below.
        const interactiveKey = this.getFreshInteractivePivot()?.itemKey;
        if (interactiveKey != null) {
            const anchor = this.captureViewportAnchor(interactiveKey);
            if (anchor != null)
                return anchor;
        }
        const centre = viewRect.height / 2;
        let best: { key: string; top: number } | null = null;
        let bestDist = Infinity;
        // `.item`, not `li.item`: grouped messages are div.item inside li.group — matching only li.item
        // made the check blind for most of a real chat. Sticky/skip rows are excluded as in pickAnchor.
        for (const li of this.containerRef.querySelectorAll<HTMLElement>('.item[data-key]')) {
            if ((li.dataset.skip != null && li.dataset.skip !== 'false')
                || window.getComputedStyle(li).position === 'sticky')
                continue;

            const r = li.getBoundingClientRect();
            if (r.height <= 0 || r.bottom <= viewRect.top || r.top >= viewRect.bottom)
                continue;

            const top = r.top - viewRect.top;
            const dist = Math.abs(top + r.height / 2 - centre);
            if (dist < bestDist) {
                bestDist = dist;
                best = { key: li.dataset.key ?? '', top };
            }
        }
        return best;
    }

    private getAllItemRefs(): HTMLElement[] {
        if (this.cachedAllItemRefs === null) {
            const elementRefs = this.containerRef.querySelectorAll<HTMLElement>(`:scope .item`);
            this.cachedAllItemRefs = Array.from(elementRefs);
        }
        return this.cachedAllItemRefs;
    }

    private getItemRef(key?: string): HTMLElement | null {
        if (key == null)
            return null;

        return this.containerRef.querySelector(`:scope .item[data-key="${key}"]`);
    }

    private getFirstItemKeyBelow(itemRef: HTMLElement): string | null {
        const all = Array.from(this.containerRef.querySelectorAll<HTMLElement>(':scope .item[data-key]'));
        const idx = all.indexOf(itemRef);
        if (idx < 0)
            return null;
        for (let i = idx + 1; i < all.length; i++) {
            const skip = all[i].dataset.skip;
            if (skip == null || skip === 'false')
                return all[i].dataset.key ?? null;
        }
        return null;
    }

    private getFirstItemRef(): HTMLElement | null {
        let ref: Element | null = this.containerRef.firstElementChild;
        while (ref && !ref.classList.contains('item') && !ref.classList.contains('group'))
            ref = ref.nextElementSibling;
        if (ref == null)
            return null;

        if (ref.classList.contains('item'))
            return ref as HTMLElement;

        if (ref.classList.contains('group')) {
            while (ref) {
                ref = ref.lastElementChild;
                if (ref?.classList.contains('item')) {
                    // we have found list item in the group, let's find the first one
                    ref = ref.parentElement!.firstElementChild;
                    return ref as HTMLElement;
                }
            }
            return null;
        }

        return null;
    }

    private getFirstItemKey(): string | null {
        return getItemKey(this.getFirstItemRef());
    }

    private getLastItemRef(): HTMLElement | null {
        // Skip trailing non-content children (end spacer, end anchor) to reach the last item/group.
        let ref: Element | null = this.containerRef.lastElementChild;
        while (ref && !ref.classList.contains('item') && !ref.classList.contains('group'))
            ref = ref.previousElementSibling;
        if (ref == null)
            return null;

        if (ref.classList.contains('item'))
            return ref as HTMLElement;

        if (ref.classList.contains('group')) {
            while (ref) {
                ref = ref.lastElementChild;
                if (ref?.classList.contains('item'))
                    return ref as HTMLElement; // we have found list item in the group, let's return it
            }
            return null;
        }

        return null;
    }

    private getLastItemKey(): string | null {
        return getItemKey(this.getLastItemRef());
    }

    private getChildItemRefs(ref: HTMLElement): HTMLElement[] {
        if (ref.classList.contains('item'))
            return [ref];

        if (ref.classList.contains('group'))
            return Array.from(ref.getElementsByClassName('item')) as HTMLElement[];

        return [];
    }

    private getChildGroupRefs(ref: HTMLElement): HTMLElement[] {
        return ref.classList.contains('group')
            ? [ref, ...Array.from(ref.getElementsByClassName('group')) as HTMLElement[]] as HTMLElement[]
            : [];
    }

    private isKeyVisible(itemKey?: string): boolean {
        if (itemKey == null)
            return false;

        return this.visibleItems.has(itemKey);
    }

    // Pass viewRect when checking several items at once - otherwise the viewport is
    // re-measured per call, which is the bulk of the geometry reads on this path.
    private isItemPartiallyVisible(itemRef: HTMLElement, viewRect?: DOMRect): boolean {
        return this.isRectPartiallyVisible(
            itemRef.getBoundingClientRect(),
            viewRect ?? this.ref.getBoundingClientRect());
    }

    private isRectPartiallyVisible(itemRect: DOMRect, viewRect: DOMRect): boolean {
        return itemRect.bottom > viewRect.top && itemRect.top < viewRect.bottom;
    }

    private isRectIntersects(rect1: DOMRect, rect2: DOMRect): boolean {
        return rect1.top < rect2.bottom && rect1.bottom > rect2.top;
    }

    private forceReflow(): void {
        this.ref.style.display = 'none';
        void this.ref.offsetWidth;
        this.ref.style.display = '';
    }

    private scrollTo(
        itemRef?: HTMLElement,
        useSmoothScroll = false,
        blockPosition: ScrollLogicalPosition = 'center') {
        debugLog?.log(`scrollTo, item key:`, getItemKey(itemRef ?? null));
        this.updateState('scrollTo', this.state, { scrollTime: Date.now() });
        if (!itemRef)
            return;
        const navigateTarget = itemRef.querySelector('div.c-author-badge') ?? itemRef;
        const vr = this.ref.getBoundingClientRect();
        const tr = navigateTarget.getBoundingClientRect();
        const elementTop = this.ref.scrollTop + (tr.top - vr.top);
        // 'center' puts the message's BEGINNING (not its middle) at the viewport center.
        const target = blockPosition === 'center' ? elementTop - vr.height / 2
            : blockPosition === 'end' ? elementTop - (vr.height - tr.height)
                : elementTop;
        this.scrollController.scrollTo(target, { smooth: useSmoothScroll });
    }

    private scrollToEdge(edge: VirtualListEdge = VirtualListEdge.End, useSmoothScroll = false, reason: ScrollToEdgeReason = 'unknown'): void {

        // debugLog?.log('scrollToEdge: schedule', edge, useSmoothScroll, reason);
        const isInitialRender = this.isInitialRender;
        if (isInitialRender && (reason === 'non-item-resize' || reason === 'item-resize'))
            return; // do not scroll to the end on initial render on spacer resize

        if (this.state.renderState.renderIndex <= 1 || isInitialRender)
            useSmoothScroll = false; // fix for scroll to the end on chat switch
        this.updateState('scrollToEdge', this.state, { scrollTime: Date.now() });

        let targetScrollTop = 0;
        // Position from the ACTUAL DOM (robust to model/DOM size drift): bring the end anchor
        // flush to the viewport bottom (End) or the first item to the viewport top (Start).
        const measureTarget = (): number => {
            const vr = this.ref.getBoundingClientRect();
            const maxScrollTop = Math.max(0, this.ref.scrollHeight - this.ref.clientHeight);
            let delta: number;
            if (edge === VirtualListEdge.Start) {
                const first = this.getFirstItemRef();
                delta = first ? first.getBoundingClientRect().top - vr.top : -this.ref.scrollTop;
            }
            else {
                // Prefer the end anchor (infinite lists — it carries the editor gap); when it's hidden
                // (finite lists, empty rect) fall back to the last item / full scroll range.
                const anchorRect = this.endAnchorRef.getBoundingClientRect();
                if (anchorRect.height > 0)
                    delta = anchorRect.bottom - vr.bottom;
                else {
                    const last = this.getLastItemRef();
                    delta = last ? last.getBoundingClientRect().bottom - vr.bottom : maxScrollTop - this.ref.scrollTop;
                }
            }

            return clamp(this.ref.scrollTop + delta, 0, maxScrollTop);
        };
        const read = () => {
            targetScrollTop = measureTarget();
            const isFarFromEdge = Math.abs(this.ref.scrollTop - targetScrollTop) > this.ref.offsetHeight;
            useSmoothScroll = useSmoothScroll && !isFarFromEdge;
        };
        const write = () => {
            // A non-smooth scrollTo forces a reflow, so re-measuring right after it sees the new geometry:
            // nudge again until flush. A smooth one animates, so its target can't be re-measured here.
            for (let pass = 0; pass < EdgeRepinMaxPasses; pass++) {
                if (Math.abs(this.ref.scrollTop - targetScrollTop) < EdgeRepinEpsilon)
                    break;

                this.scrollController.scrollTo(targetScrollTop, { smooth: useSmoothScroll });
                if (useSmoothScroll)
                    break;

                targetScrollTop = measureTarget();
            }
            if (edge == VirtualListEdge.End) {
                void this.turnOnIsEndAnchorVisible();
                this.turnOffIsEndAnchorVisibleDebounced.reset();
            }
            debugLog?.log('scrollToEdge: complete', edge, useSmoothScroll, reason);
        };
        // A viewport resize arrives inside a ResizeObserver callback — after layout, before paint — so a
        // synchronous re-pin lands the correction in the same frame the viewport changed. The fastRaf path
        // defers read→write by a frame, which lets the edge visibly drift while a sub-header animates.
        if (reason === 'viewport-resize') {
            read();
            write();
        }
        else
            fastRaf({ read, write });
    }

    // True when the viewport is still flush with the current sticky edge (End: end anchor visible;
    // Start: the first item's top is at/above the viewport top).
    private isAtStickyEdge(): boolean {
        const edge = this.state.stickyEdge?.edge;
        if (edge == null)
            return false;
        if (edge === VirtualListEdge.End)
            return this.isAtEndEdge();

        const first = this.getFirstItemRef();
        if (!first)
            return false;

        return first.getBoundingClientRect().top >= this.ref.getBoundingClientRect().top - VisibilityEpsilon;
    }

    // Measured, never read off isEndAnchorVisible. That flag is debounced on the way out AND scrollToEdge
    // re-arms it on every re-pin, so it stays true while the user scrolls away - which let the End edge be
    // both kept and re-established mid-fling, and each re-pin re-armed the flag again. Geometry can't be
    // self-sustaining that way.
    private isAtEndEdge(): boolean {
        const bottom = this.ref.getBoundingClientRect().bottom;
        const anchorRect = this.endAnchorRef.getBoundingClientRect();
        if (anchorRect.height > 0)
            return anchorRect.bottom <= bottom + VisibilityEpsilon;

        const last = this.getLastItemRef();
        return last != null && last.getBoundingClientRect().bottom <= bottom + VisibilityEpsilon;
    }

    private setStickyEdge(stickyEdge: VirtualListStickyEdgeState | null): boolean {
        if (stickyEdge && !stickyEdge.itemKey)
            return false; // itemKey is undefined

        const old = this.state.stickyEdge;
        if (old?.itemKey !== stickyEdge?.itemKey || old?.edge !== stickyEdge?.edge) {
            debugLog?.log(`setStickyEdge:`, stickyEdge);
            this.updateState('setStickyEdge', this.state, { stickyEdge: stickyEdge as Required<VirtualListStickyEdgeState> | null });

            // Toggle class for CSS transition control
            const addStickyEnd = stickyEdge?.edge === VirtualListEdge.End;
            fastRaf({
                write: () => {
                    if (addStickyEnd) {
                        this.ref.classList.add('sticky-end');
                    } else {
                        this.ref.classList.remove('sticky-end');
                    }
                },
            });

            if (stickyEdge?.edge === VirtualListEdge.End) {
                const lastItemRef = this.getLastItemRef();
                if (!lastItemRef)
                    return false;

                let hasAnchor = false;
                fastRaf({
                    read: () => {
                        hasAnchor = lastItemRef.classList.contains('anchor');
                    },
                    write: () => {
                        if (!hasAnchor)
                            lastItemRef.classList.add('anchor');
                    },
                });
            }
            return true;
        }
        return false;
    }

    // Computed on demand by ScrollController (so it always reflects the latest item sizes, edges, and
    // viewport height — incl. mobile keyboard). Reads the model + current container.top, no re-measure.
    private computeScrollLimits(): { min: number | null, max: number | null } {
        const itemRange = this.state.itemRange;
        if (!itemRange)
            return { min: null, max: null };

        const rs = this.state.renderState;
        const clientHeight = this.ref.clientHeight;
        const containerTop = parseFloat(this.containerRef.style.top) || 0;
        // A limit exists only at a discovered edge; with no edge that way it's null => free scroll there
        // (more items load in as you go).
        let min = rs.hasVeryFirstItem ? containerTop : null;
        let max = rs.hasVeryLastItem ? itemRange.end + this.state.endAnchorSize - clientHeight : null;
        // Whole chat fits the viewport (both edges known, band inverts) => collapse to the preferred edge.
        if (min != null && max != null && min > max) {
            if (this.defaultEdge === VirtualListEdge.End)
                min = max;
            else
                max = min;
        }
        return { min, max };
    }

    private async syncLayoutAfterRender(rs: VirtualListRenderState, scrollIntent: ScrollIntent | null = null, useRaf = false): Promise<void> {
        const { endAnchorSize } = this.state;
        const { hasUnmeasuredItems, defaultSpacerSize } = this;
        const result = new PromiseSource();
        // debugLog?.log(`syncLayoutAfterRender: start`);

        let scrollTop = 0;
        let scrollTopOffset = 0;
        let offset = 0;
        let totalSize = 0;
        let spacerSize = 0;
        let endSpacerSize = 0;
        let totalSizeDiff = 0;
        let bottomCapped = false;
        // No-jump check (debug only): a visible anchored item must keep its screen position across a
        // render that isn't an intentional scroll. Captured before re-layout, compared after.
        let jumpAnchor: { key: string; top: number } | null = null;
        const hasInteractiveLayoutAnchor = this.getFreshInteractivePivot() != null
            && scrollIntent?.reason !== 'sticky-edge'
            && scrollIntent?.reason !== 'last-item'
            && scrollIntent?.reason !== 'item';

        // Cancel any pending viewport calculations
        this.updateViewportThrottled.reset();

        const options = {
            key: `syncLayoutAfterRender_${this.identity}`,
            read: () => {
                if (this.debug)
                    jumpAnchor = this.debug.takePreRenderAnchor() ?? this.captureViewportAnchor();
                // Pivots anchor the item-range coords across re-measurement (a pivot's range stays fixed).
                // They're cleared on scroll and only re-made on interactive events, so wheel/mouse leaves
                // none — without one the ranges recompute from scratch and the viewport jumps. Force one
                // from the visible items first (mid-render, but the DOM is already laid out).
                if (this.state.pivots.length === 0)
                    this.updateCurrentPivots(undefined, true);
                if (hasUnmeasuredItems)
                    this.measureItems();
                if (!this.state.itemRange)
                    this.ensureItemRangeCalculated();

                const ir0 = this.state.itemRange ?? new NumberRange(0, 0);
                const start = ir0.start;
                let end = ir0.end;
                const itemRangeSize = ir0.size;
                const oldTotalSize = this.wrapperRef.offsetHeight;

                scrollTop = this.ref.scrollTop;

                totalSize = InfiniteSize; // fixed huge scroll space; chain floats around the middle

                // offset = wrapper coordinate of the first loaded item; container.top = offset - startSpacer.
                offset = start;

                const reCenter = () => {
                    const resetDelta = this.rebuildItemRangeFromAnchor();
                    if (resetDelta !== null) {
                        scrollTopOffset = resetDelta;
                        end = this.state.itemRange!.end;
                        offset = this.state.itemRange!.start;
                    }
                };

                // Re-center only if the chain drifted within a viewport of a wrapper edge (fast fling).
                const margin = Math.max(this.ref.clientHeight, defaultSpacerSize);
                if (start < margin || end > InfiniteSize - margin)
                    reCenter();

                // Bottom cap: clip the wrapper to the newest so scrolling down hard-stops there natively (no
                // chain reposition — safe, unlike a top cap). After re-center so `end` is final. End-edge only:
                // on a Start-edge list this cap leaves no room below short content to scroll its first item up
                // to the viewport top, stranding a single item / the empty placeholder at the bottom.
                if (rs.hasVeryLastItem && CutVirtualSpaceAtBottom
                    && this.defaultEdge === VirtualListEdge.End) {
                    totalSize = end + endAnchorSize;
                    bottomCapped = true;
                }

                totalSizeDiff = totalSize - oldTotalSize;

                spacerSize = rs.hasVeryFirstItem ? 0 : clamp(offset, 0, defaultSpacerSize);
                endSpacerSize = rs.hasVeryLastItem
                    ? 0
                    : clamp(oldTotalSize - itemRangeSize - spacerSize - endAnchorSize, 0, defaultSpacerSize);
                offset -= spacerSize;

                // Set lastProgrammaticScrollAt BEFORE write/scroll to guard onScroll from false "user scroll" detection
                this.updateState('programmaticScroll', this.state, { lastProgrammaticScrollAt: Date.now() });
            },
            write: () => {
                const showSpacer = spacerSize > 0;
                const showEndSpacer = endSpacerSize > 0;
                if (!showSpacer)
                    this.spacerRef.style.display = 'none';
                else
                    this.spacerRef.style.display = 'flex';
                if (!showEndSpacer)
                    this.endSpacerRef.style.display = 'none';
                else
                    this.endSpacerRef.style.display = 'flex';
                this.spacerRef.style.height = `${spacerSize}px`;
                this.endSpacerRef.style.height = `${endSpacerSize}px`;

                if (bottomCapped) {
                    // The cap must stick: drop any deferred (stale, larger) height and apply now, else a
                    // pending turnOffScrollingCallback restores InfiniteSize and lets you scroll past newest.
                    this.turnOffScrollingCallback = undefined;
                    if (totalSizeDiff != 0)
                        this.wrapperRef.style.height = `${totalSize}px`;
                }
                else if (totalSizeDiff != 0 && this.state.isScrolling && rs.renderIndex > 0) {
                    // delay wrapper size change while scrolling in Chromium to prevent scroll position jumps
                    const setWrapperHeight = () => fastRaf({
                        write: () => {
                            if (this.state.isScrolling)
                                this.turnOffScrollingCallback = setWrapperHeight;
                            else
                                this.wrapperRef.style.height = `${totalSize}px`;
                        } });
                    this.turnOffScrollingCallback = setWrapperHeight;
                }
                else if (totalSizeDiff != 0) {
                    this.wrapperRef.style.height = `${totalSize}px`;
                }

                this.containerRef.style.top = `${offset}px`;
                // Compensate scrollTop after a re-anchor so the view doesn't visibly jump (chain and
                // scrollTop shift by the same delta).
                if (scrollTopOffset)
                    this.scrollController.scrollTo(scrollTop + scrollTopOffset, { smooth: false });

                this.applyScrollIntent(scrollIntent, hasInteractiveLayoutAnchor);
                this.updateViewportThrottled();

                // No-jump check (debug): a previously-visible anchored item must keep its screen position
                // across a non-scroll render; if it drifted, the scrollTop compensation was wrong.
                if (this.debug && jumpAnchor && !scrollIntent?.scroll) {
                    const after = this.captureViewportAnchor(jumpAnchor.key);
                    if (after != null) {
                        const drift = after.top - jumpAnchor.top;
                        if (Math.abs(drift) > 8)
                            this.debug.noteRenderJump({
                                key: jumpAnchor.key,
                                before: Math.round(jumpAnchor.top),
                                after: Math.round(after.top),
                                drift: Math.round(drift),
                                reason: scrollIntent?.reason ?? 'none',
                                renderIndex: rs.renderIndex,
                            });
                    }
                }

                // debugLog?.log(`syncLayoutAfterRender: scroll set`, offset, totalSize, scrollTop, spacerSize, endSpacerSize);

                this.scrollController.clampToLimits();

                result.resolve(undefined);
            }
        };

        if (useRaf) {
            fastRaf(options);
        } else {
            options.read();
            options.write();
        }
        await result;
    }

    private applyScrollIntent(scrollIntent: ScrollIntent | null, hasInteractiveLayoutAnchor: boolean): void {
        if (hasInteractiveLayoutAnchor) {
            debugLog?.log(`applyScrollIntent: held by interactive pivot`, scrollIntent?.reason);
            return;
        }

        scrollIntent?.scroll?.();
        debugLog?.log(`applyScrollIntent: scroll set synchronously`, scrollIntent?.reason);
    }

    // No scrollTop change: the cornerstone is held fixed, so writing containerTop alone keeps the view put.
    private repinContainerToModel(): void {
        const itemRange = this.state.itemRange;
        if (!itemRange)
            return;

        const rs = this.state.renderState;
        const { defaultSpacerSize } = this;
        const oldTotalSize = this.wrapperRef.offsetHeight;
        const spacerSize = rs.hasVeryFirstItem ? 0 : clamp(itemRange.start, 0, defaultSpacerSize);
        const endSpacerSize = rs.hasVeryLastItem
            ? 0
            : clamp(oldTotalSize - itemRange.size - spacerSize - this.state.endAnchorSize, 0, defaultSpacerSize);
        this.spacerRef.style.height = `${spacerSize}px`;
        this.endSpacerRef.style.height = `${endSpacerSize}px`;
        this.containerRef.style.top = `${itemRange.start - spacerSize}px`;
    }

    private measureItems(): void {
        if (!this.hasUnmeasuredItems)
            return;

        const unmeasuredItems = [...this.unmeasuredItems];
        let itemsWereMeasured = false;
        const removeUnmeasuredItem = (key: string): void => {
            const wasRemoved = this.unmeasuredItems.delete(key);
            itemsWereMeasured ||= wasRemoved;
        };

        for (const key of unmeasuredItems) {
            const item = this.items.get(key);
            if (!item) {
                removeUnmeasuredItem(key);
                continue;
            }

            const itemSizeIsValid = item.size && item.size > 0;
            if (itemSizeIsValid) {
                removeUnmeasuredItem(key);
                continue;
            }

            const itemRef = this.getItemRef(key);
            if (!itemRef) {
                this.items.delete(key);
                removeUnmeasuredItem(key);
                continue;
            }

            const boundingRect = itemRef.getBoundingClientRect();
            const size = Math.ceil(boundingRect.height + this.rowGap);

            if (size > 0 || isLaidOut(itemRef)) {
                item.size = size;
                if (item.range)
                    item.range = new NumberRange(item.range.start, item.range.start + size);
                itemsWereMeasured = true;
                this.sizeCache.set(key, size);
                removeUnmeasuredItem(key);
            }
        }

        // recalculate item range as some elements were updated
        if (itemsWereMeasured) {
            this.updateState('measureItems', this.state, { itemRange: null });
            this.ensureItemRangeCalculated();
        }
    }

    private ensureItemRangeCalculated(): boolean {
        // this function is expected to be called with RAF
        if (this.hasUnmeasuredItems) {
            this.measureItems();
        }

        if (this.state.itemRange)
            return false;

        const { renderState: rs, orderedItems } = this.state;
        const { visibleItems, defaultEdge, statistics } = this;

        // nothing to do when there are no items rendered
        if (orderedItems.length == 0)
            return false;

        let cornerstoneItemIndex = -1;
        let cornerstoneItem: VirtualListItem | null = null;
        const interactivePivot = this.getFreshInteractivePivot();
        if (interactivePivot) {
            // Use interactive pivot as cornerstone item
            const itemKey = interactivePivot.itemKey;
            cornerstoneItemIndex = orderedItems.findIndex(i => i.key === itemKey);
            // ordered items might be re-built after render
            cornerstoneItem = orderedItems[cornerstoneItemIndex] ?? null;
            if (cornerstoneItem?.range && interactivePivot.stickyOffset) {
                // adjust cornerstone item range based on sticky offset
                const offsetDelta = interactivePivot.stickyOffset;
                cornerstoneItem.range = new NumberRange(
                    cornerstoneItem.range.start + offsetDelta,
                    cornerstoneItem.range.end + offsetDelta);
                interactivePivot.stickyOffset = null;
            }
            // Re-measure interactive cornerstone: DOM may have been updated by Blazor
            // but ResizeObserver hasn't fired yet, so item.size can be stale
            if (cornerstoneItem?.range) {
                const itemRef = this.getItemRef(cornerstoneItem.key);
                if (itemRef) {
                    const rect = itemRef.getBoundingClientRect();
                    const measuredSize = Math.ceil(rect.height + this.rowGap);
                    if (measuredSize > 0 && measuredSize !== cornerstoneItem.size) {
                        const oldSize = cornerstoneItem.size ?? 0;
                        const cornerstoneSizeDiff = Math.abs(measuredSize - oldSize);
                        const isDiffSmall = cornerstoneSizeDiff < measuredSize / 2 && cornerstoneSizeDiff < oldSize / 2;
                        cornerstoneItem.size = measuredSize;
                        if (this.defaultEdge === VirtualListEdge.End && isDiffSmall) {
                            // Start from the end for smaller diffs
                            cornerstoneItem.range = new NumberRange(
                                cornerstoneItem.range.end - measuredSize,
                                cornerstoneItem.range.end);
                        }
                        else {
                            // this change is usually caused by conversation expansion or significant message rewrite
                            cornerstoneItem.range = new NumberRange(
                                cornerstoneItem.range.start,
                                cornerstoneItem.range.start + measuredSize);
                        }
                        this.sizeCache.set(cornerstoneItem.key, measuredSize);
                    }
                }
            }
        }
        const visibleItemKeys = [...visibleItems.keys()]
            .map(k => this.items.get(k))
            .filter(i => i?.range)
            .sort((a, b) => (a!.range?.start ?? 0) - (b!.range?.start ?? 0))
            .map(i => i!.key);
        if (!cornerstoneItem && visibleItemKeys.length > 0) {
            // Cornerstone = the viewport-CENTER visible item (its range is kept fixed across re-layout),
            // not the topmost — anchoring the top lets a size change above the centre shift what's on screen.
            const mid = Math.floor(visibleItemKeys.length / 2);
            for (let d = 0; d < visibleItemKeys.length; d++) {
                const idx = d % 2 === 0 ? mid + d / 2 : mid - (d + 1) / 2;
                if (idx < 0 || idx >= visibleItemKeys.length)
                    continue;
                const index = orderedItems.findIndex(i => i.key === visibleItemKeys[idx] && i.range);
                if (index !== -1) {
                    cornerstoneItemIndex = index;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                    break;
                }
            }
        }
        if (!cornerstoneItem?.range) {
            if (this.defaultEdge === VirtualListEdge.End) {
                cornerstoneItemIndex = orderedItems.length - 1;
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
                // Find first one from the end
                while (!cornerstoneItem.range && cornerstoneItemIndex > 0) {
                    cornerstoneItemIndex--;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                }
            } else if (this.defaultEdge === VirtualListEdge.Start) {
                cornerstoneItemIndex = 0;
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
                // Find first one from the start
                while (!cornerstoneItem.range && cornerstoneItemIndex < orderedItems.length - 1) {
                    cornerstoneItemIndex++;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                }
            }
        }

        const needsRangeReset = !cornerstoneItem?.range;
        if (needsRangeReset) {
            // We have checked all items and there is no cornerstone item, so let's recalculate all ranges
            this.rebuildItemRangeFromAnchor(true);
        }
        else
            this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);

        if (orderedItems.some(i => i.range == null))
            return false;

        const newItemRange = new NumberRange(
            orderedItems[0].range!.start,
            orderedItems[orderedItems.length - 1].range!.end - this.rowGap);

        const changes: Partial<VirtualListState> = {
            itemRange: newItemRange,
            minStart: Math.min(this.state.minStart ?? Number.MAX_SAFE_INTEGER, newItemRange.start),
            maxEnd: Math.max(this.state.maxEnd ?? Number.MIN_SAFE_INTEGER, newItemRange.end),
        };
        if (this.state.renderState.hasVeryFirstItem)
            changes.isStartKnown = true;
        if (this.state.renderState.hasVeryLastItem)
            changes.isEndKnown = true;
        this.updateState('ensureItemRangeCalculated', this.state, changes);

        return true;
    }

    private recalculateItemRangesFromCornerstone(orderedItems: VirtualListItem[], cornerstoneItemIndex: number): void {
        const cornerstoneItem = orderedItems[cornerstoneItemIndex];
        let prevItem = cornerstoneItem;
        for (let i = cornerstoneItemIndex + 1; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range!.end, prevItem.range!.end + item.size!);
            prevItem = item;
        }
        prevItem = cornerstoneItem;
        for (let i = cornerstoneItemIndex - 1; i >= 0; i--) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range!.start - item.size!, prevItem.range!.start);
            prevItem = item;
        }
    }

    private rebuildItemRangeFromAnchor(canUseViewport = false): number | null {
        // This function is expected to be called with RAF
        const { orderedItems, endAnchorSize, renderState: rs } = this.state;
        const { defaultSpacerSize } = this;
        const fullRangeSize = this.knownRange?.size;

        let viewport = this.calculateViewport();
        if (viewport === null && this.state.viewport == null)
            return null; // viewport is not ready yet

        // Use current viewport if new one is not available
        if (viewport != null)
            this.updateState('rebuildItemRangeFromAnchor: viewport', this.state, { viewport });
        else
            viewport = this.state.viewport!;

        if (orderedItems.length === 0)
            return null;

        let rangeDelta: number | null = null;
        // eslint-disable-next-line @typescript-eslint/no-misused-spread
        const originalRanges = orderedItems.map(item => ({ ...item.range }) as Range<number>);

        function findCenterItemIndex() {
            // Find item index closest to the viewport center
            const totalSize = orderedItems.reduce((sum, item) => sum + item.size!, 0);
            let runningSize = 0;
            let cornerstoneItemIndex = 0;
            for (let i = 0; i < orderedItems.length; i++) {
                runningSize += orderedItems[i].size!;
                if (runningSize >= totalSize / 2) {
                    cornerstoneItemIndex = i;
                    break;
                }
            }
            return cornerstoneItemIndex;
        }

        let cornerstoneItemIndex = 0;
        let cornerstoneItem = orderedItems[0];

        // Center the chain in the virtual space. On a re-anchor it rigidly shifts the whole chain by a
        // fixed delta (returned); syncLayoutAfterRender shifts scrollTop by the same amount, so no jump.
        cornerstoneItemIndex = findCenterItemIndex();
        cornerstoneItem = orderedItems[cornerstoneItemIndex];
        const half = Math.floor(cornerstoneItem.size! / 2);
        const base = Math.round(InfiniteSize / 2);
        cornerstoneItem.range = new NumberRange(base - half, base - half + cornerstoneItem.size!);

        this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);
        // The rigid-shift delta is only computable from items that had a range before the rebuild; a
        // genuinely new item set has none — return null then, not NaN (NaN silently disabled the
        // scrollTop compensation in reCenter while the chain still moved).
        const shiftDeltas = originalRanges
            .map((r, i) => r.start != null ? orderedItems[i].range!.start - r.start : null)
            .filter((d): d is number => d != null && !Number.isNaN(d));
        rangeDelta = shiftDeltas.length > 0 ? Math.max(...shiftDeltas) : null;
        const newItemRange = new NumberRange(
            orderedItems[0].range!.start,
            orderedItems[orderedItems.length - 1].range!.end);
        this.updateState('rebuildItemRangeFromAnchor', this.state, {
            itemRange: newItemRange,
            minStart: newItemRange.start,
            maxEnd: newItemRange.end,
            isEndKnown: rs.hasVeryLastItem,
            isStartKnown: rs.hasVeryFirstItem,
        });
        return rangeDelta;
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this.state.viewport)
            return;

        const query = this.buildDataQuery();
        // if (this.state.renderState.renderIndex > 0)
        //     return;// this.state.lastQuery; // Debug helper
        if (!this.mustRequestData(query)) {
            // debugLog?.log(`requestData: request is unnecessary`);
            return;
        }
        if (query.isNone)
            return;

        this.updateState('requestData: query', this.state, { query });

        const whenRequestDataCompleted = this.whenRequestDataCompleted;
        if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted) {
            debugLog?.log(`requestData: the previous request is not completed yet`);
            return;
        }

        const newWhenRequestDataCompleted = new PromiseSourceWithTimeout<void>();
        newWhenRequestDataCompleted.setTimeout(RequestDataTimeout, () => {
            newWhenRequestDataCompleted.resolve(undefined);
        });
        this.whenRequestDataCompleted = newWhenRequestDataCompleted;

        debugLog?.log(`requestData: query:`, query, query.virtualRange, this.state.itemRange);
        this.updateState('requestData: sending', this.state, { lastQueryTime: Date.now(), lastQuery: this.state.query });
        warnLog?.log(`Data request: ${this.renderedWindowInfo()} query.expected=${query.expectedCount ?? '?'}`);
        if (InfiniteList.debugDataLoadDelayMs > 0)
            await delayMs(Math.random() * InfiniteList.debugDataLoadDelayMs);
        this.debug?.onRequestData();
        await this.blazorRef.invokeMethodAsync('RequestData', this.state.query);
    }

    private mustRequestData(query: VirtualListDataQuery): boolean {
        const queryRange = query.virtualRange;
        const { itemRange, viewport, renderState: rs } = this.state;
        if (!itemRange || !queryRange)
            return false;

        if (!viewport)
            return false;

        if (itemRange.isEmpty)
            return true; // re-request data with empty query

        if (rs.query === query || this.state.lastQuery === query)
            return false;

        // When skeletons are visible, accept any genuinely new query —
        // the pixel-distance heuristics below can miss edge cases.
        if (this.state.isNearSkeleton)
            return true;

        if (itemRange.contains(query.virtualRange))
            return false;

        if (query.moveRange.start === 0 && query.moveRange.end === 0)
            return false;

        const viewportSize = viewport.size;
        const commonRange = itemRange.intersectWith(queryRange);
        if (commonRange.isEmpty)
            return true;

        const isLoadingStart = commonRange.start - queryRange.start > viewportSize / 2;  // we are loading more than half of viewport at the start edge
        const isLoadingEnd = queryRange.end - commonRange.end > viewportSize / 2; // we are loading more than half of viewport at the end edge
        const isViewportCloseToStart = !rs.hasVeryFirstItem && Math.abs(viewport.start - itemRange.start) < viewportSize; // viewport is close to the start edge and there are items above
        const isViewportCloseToEnd = !rs.hasVeryLastItem && Math.abs(itemRange.end - viewport.end) < viewportSize; // viewport is close to the end edge and there are items bellow
        const isEdgeItemInViewport = viewport.contains(itemRange.start) || viewport.contains(itemRange.end);
        const isNotEnoughItemsToFulfillViewport = viewport.intersectWith(itemRange).size < viewportSize * 0.9;
        const isInitialRender = this.isInitialRender;

        const mustExpand =
            !isInitialRender && (isLoadingStart && isViewportCloseToStart || isLoadingEnd && isViewportCloseToEnd)
            || isEdgeItemInViewport
            || isNotEnoughItemsToFulfillViewport;
        // NOTE(AY): The condition below checks just one side
        const mustContract = !isInitialRender && Math.abs(itemRange.end - commonRange.end) > viewportSize;
        return mustExpand || mustContract;
    }

    private buildDataQuery(): VirtualListDataQuery {
        const rs = this.state.renderState;
        // if (rs.renderIndex > 0)
        //     return this.state.lastQuery; // Debug helper

        const itemSize = this.statistics.itemSize;
        const viewport = this.state.viewport;
        this.ensureItemRangeCalculated();
        const orderedItems = [...this.state.orderedItems.filter(i => !i.shouldSkipKey)];
        if (orderedItems.length == 0) // No entries -> nothing to "align" the query to
            return this.state.lastQuery;

        if (orderedItems.some(item => item.range == null)) {
            this.updateState('buildDataQuery: invalidate', this.state, { itemRange: null });
            this.ensureItemRangeCalculated();
        }

        const alreadyLoaded = this.state.itemRange;
        if (!viewport || !alreadyLoaded)
            return this.state.lastQuery;

        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return this.state.lastQuery; // We have already loaded all data

        if (this.isRendering)
            return this.state.lastQuery; // Do not request data during rendering as it might be inconsistent

        const now = Date.now();
        if (now - this.state.renderCompletedAt < 500 && this.state.lastQuery.isNone)
            return this.state.lastQuery; // Do not request data during the first second after render caused by updated data (not scroll)

        const viewportSize = viewport.size;
        const alreadyLoadedFromStart = viewport.start - alreadyLoaded.start;
        const alreadyLoadedTillEnd = alreadyLoaded.end - viewport.end;
        const loadZoneTrigger = viewportSize * this.expandMultiplier * 0.5;
        if (alreadyLoadedFromStart > loadZoneTrigger && alreadyLoadedTillEnd > loadZoneTrigger
            && !this.state.isNearSkeleton)
            return this.state.lastQuery; // No need to load more data

        const loadZoneSize = viewportSize * this.expandMultiplier;
        let loadStart = viewport.start - loadZoneSize;
        let loadEnd = viewport.end + loadZoneSize;

        // adjust to existing data range
        if (loadStart < alreadyLoaded.start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.start;
        if (loadEnd > alreadyLoaded.end && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.end;

        const anchors = this.getQueryRetainedItems(orderedItems, viewport);
        if (anchors.length) {
            loadStart = Math.min(loadStart, anchors[0].range!.start);
            loadEnd = Math.max(loadEnd, anchors[anchors.length - 1].range!.end);
        }

        const loadZone = new NumberRange(loadStart, loadEnd);
        if (alreadyLoaded.contains(loadZone)) {
            // debug helper
            // console.warn('already!', viewport, alreadyLoaded, loadZone);
            return this.state.lastQuery;
        }

        const lastKey = orderedItems[orderedItems.length - 1].key;
        const firstItemIndex = binarySearch(orderedItems, item => item.range!.end >= loadZone.start);
        const lastItemIndex = binarySearch(orderedItems, item => item.range!.start > loadZone.end || (item.key === lastKey && !item.range!.intersectWith(loadZone).isEmpty));
        let firstItem = orderedItems[firstItemIndex];
        let lastItem = orderedItems[lastItemIndex];
        if (!firstItem) {
            if (orderedItems[0].range!.start >= loadZone.end)
                firstItem = orderedItems[0];
            else if (orderedItems[orderedItems.length - 1].range!.end <= loadZone.start)
                firstItem = orderedItems[orderedItems.length - 1];
            else
                firstItem = orderedItems[0];
        }
        if (!lastItem) {
            if (orderedItems[orderedItems.length - 1].range!.end <= loadZone.start)
                lastItem = orderedItems[orderedItems.length - 1];
            else if (orderedItems[0].range!.start >= loadZone.end)
                lastItem = orderedItems[0];
            else
                lastItem = orderedItems[orderedItems.length - 1];
        }
        const keyRange = new Range(firstItem.key, lastItem.key);
        const moveRangeStart = Math.floor((loadZone.start - firstItem.range!.start) / itemSize / 5) * 5; // round to 5 to prevent too many requests
        const moveRangeEnd = Math.ceil((loadZone.end - lastItem.range!.end) / itemSize / 5) * 5; // round to 5 to prevent too many requests
        const moveRange = new NumberRange(moveRangeStart, moveRangeEnd);
        const startGap = Math.max(0, firstItem.range!.start - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastItem.range!.end);
        // skip queries that load few more items - we prefer to load more - if not close of the skeletons
        const smallGap = viewportSize * 0.5;
        const isFirstItemInViewport = !rs.hasVeryFirstItem && firstItem.range!.end >= viewport.start;
        const isLastItemInViewport = !rs.hasVeryLastItem && lastItem.range!.start <= viewport.end;
        if (startGap < smallGap && endGap < smallGap && firstItem.range!.start && !isFirstItemInViewport && !isLastItemInViewport
            && !this.state.isNearSkeleton)
            return this.state.lastQuery;

        const query = new VirtualListDataQuery(keyRange, loadZone, moveRange);
        query.expectedCount = Math.ceil(loadZone.size / this.statistics.itemSize);
        return query;
    }

    private getQueryRetainedItems(orderedItems: VirtualListItem[], viewport: NumberRange): VirtualListItem[] {
        const withRange = orderedItems.filter(i => i.range != null);
        const visible = withRange.filter(i => i.range!.end > viewport.start && i.range!.start < viewport.end);
        if (visible.length)
            return visible;
        const center = (viewport.start + viewport.end) / 2;
        return [...withRange]
            .sort((a, b) =>
                Math.abs((a.range!.start + a.range!.end) / 2 - center) - Math.abs((b.range!.start + b.range!.end) / 2 - center))
            .slice(0, this.retainedItemCount)
            .sort((a, b) => a.range!.start - b.range!.start);
    }
}

// Helper functions
function getItemKey(itemRef: HTMLElement | null): string | null {
    return itemRef?.dataset?.key ?? null;
}

// Tells a 0px measurement that is a bad read (display:none, detached mid-render) from one that is
// the truth: an item that is laid out and simply renders nothing generates a box, it is just empty.
// The live conversation card is exactly that once its block is expanded, and keeping its last
// non-zero size reserves that many px of unreachable space below the newest message.
function isLaidOut(itemRef: HTMLElement): boolean {
    return itemRef.getClientRects().length > 0;
}

/**
 * Return 0 <= i <= array.length such that !pred(array[i - 1]) && pred(array[i]).
 */
function binarySearch<T>(array: T[], pred: (item: T) => boolean): number {
    let low = -1;
    let high = array.length;
    while (1 + low < high) {
        const mid = low + ((high - low) >> 1);
        if (pred(array[mid])) {
            high = mid;
        } else {
            low = mid;
        }
    }
    if (high == array.length)
        return -1;

    return high;
}

function getOriginalPosition(element: HTMLElement): number {
    // Store original inline styles
    const originalPosition = element.style.position;
    const originalTop = element.style.top;

    // Temporarily set to static and remove top/left
    element.style.position = 'static';
    element.style.top = '';

    // Calculate static position
    const rect = element.getBoundingClientRect();
    const staticTop = rect.top;

    // Restore original inline styles
    element.style.position = originalPosition;
    element.style.top = originalTop;

    return staticTop;
}
