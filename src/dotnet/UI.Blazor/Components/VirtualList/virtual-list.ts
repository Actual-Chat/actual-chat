/* eslint-disable @typescript-eslint/no-unused-vars,@typescript-eslint/no-unnecessary-condition */
import { debounce, PromiseSource, PromiseSourceWithTimeout, throttle } from 'actuallab-core';
import { NumberRange, Range } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { Pivot } from './ts/pivot';
import { DotNet } from '@microsoft/dotnet-js-interop';

import { getLogs } from 'logging';
import { fastRaf, fastReadRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';
import { clamp } from 'math';
import { BrowserInfo } from '../../Services/BrowserInfo/browser-info';
import { DocumentEvents } from 'event-handling';
import { type Subscription } from 'rxjs';

const { warnLog, debugLog } = getLogs('VirtualList');

const UpdateViewportInterval = 64;
const UpdateItemVisibilityInterval = 250;
const VisibilityEpsilon = 4;
const EdgeEpsilon = 4;
const ScrollDebounce = 200;
const ScrollRestoreGuard = DeviceInfo.isMobile ? 250 : 100;
const SkeletonDetectionBoundary = 200;
const MinViewPortSize = 400;
const RequestDataTimeout = 800;

type ScrollToEdgeReason = 'no-pivot' | 'last-item' | 'item' | 'sticky-edge' | 'non-item-resize' | 'item-resize' | 'unknown';
interface ScrollMetadata {
    shouldUseSmoothScroll: boolean;
    scrollType: ScrollToEdgeReason;
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
    scrollPositionRestoredAt: number;
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

export class VirtualList {
    private static readonly _instances = new Set<VirtualList>();
    public static enableWatchdogFixes = false;

    public static dumpStateChangeLogs(lastN?: number, endStateEvery = 10): void {
        for (const instance of VirtualList._instances) {
            console.warn(
                `[VirtualList:${instance.identity}] State history:`,
                instance.getStateChangeLog(lastN, endStateEvery));
        }
    }

    /** ref to div.virtual-list */
    private readonly createdAt: number;
    private readonly ref: HTMLElement;
    private readonly containerRef: HTMLElement;
    private readonly renderStateRef: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly identity: string;
    private readonly defaultEdge: VirtualListEdge;
    private readonly defaultSpacerSize: number;
    private readonly expandMultiplier: number;
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
    private skeletonWatchdogTimer: ReturnType<typeof setInterval> | null = null;
    private skeletonWatchdogLastVersion = -1;
    private userScrollDirection: 'up' | 'down' | 'none' = 'none';
    private _restoreScrollPositionPending = false;

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
    ) {
        return new VirtualList(
            ref,
            backendRef,
            identity,
            defaultEdge,
            spacerSize,
            expandMultiplier);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        spacerSize: number,
        expandMultiplier: number,
    ) {
        if (debugLog) {
            debugLog?.log(`constructor`);
            globalThis.virtualList = this;
        }
        globalThis['VirtualList'] = VirtualList;

        this.createdAt = Date.now();
        this.ref = ref;
        this.blazorRef = backendRef;
        this.identity = identity;
        this.defaultEdge = defaultEdge;
        this.defaultSpacerSize = spacerSize;
        this.expandMultiplier = expandMultiplier;

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
        this.endAnchorRef = this.wrapperRef.querySelector(':scope > .c-end-anchor')!;
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;

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
            scrollPositionRestoredAt: 0,
            isNearSkeleton: false,
            isEndAnchorVisible: false,
            endAnchorSize: this.endAnchorRef.getBoundingClientRect().height,
            stickyEdge: null,
            isUpdatingPivots: false,
        };

        // Set positioning according to the default edge
        if (this.defaultEdge === VirtualListEdge.Start) {
            this.ref.style.flexDirection = 'column';
        }
        else {
            this.ref.style.flexDirection = 'column-reverse';
        }

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
        // An array of numbers between 0.0 and 1.0, specifying a ratio of intersection area to total bounding box area for the observed target.
        // Trigger callbacks as early as it can on any intersection change, even 1 percent
        // 0 threshold doesn't work, despite what is written in the documentation
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
            // update pivots just before the render
            // we can do this because Blazor updates attributes before changing nodes
            // we SHOULD NOT fail there - otherwise Blazor will fail
            try {
                const time = Date.now();
                debugLog?.log(`renderStartedAt: `, time, value);
                this.updateState('renderIndex.setAttribute', this.state, { renderStartedAt: time });
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

        VirtualList._instances.add(this);
        this.skeletonWatchdogTimer = setInterval(() => this.checkSkeletonWatchdog(), SkeletonWatchdogInterval);
    };

    /** Called by blazor */
    public dispose() {
        debugLog?.log(`dispose()`);
        this.isDisposed = true;
        VirtualList._instances.delete(this);
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

        // itemRange jumping significantly after resetItemRange
        if (changedFields.includes('itemRange') && prev.itemRange && next.itemRange) {
            const rangeJump = Math.abs(next.itemRange.start - prev.itemRange.start);
            const rangeSize = Math.max(prev.itemRange.size, next.itemRange.size);
            if (rangeSize > 0 && rangeJump > rangeSize * 2)
                warnings.push(`itemRange jumped ${rangeJump}px (${(rangeJump / rangeSize).toFixed(1)}x range size): `
                    + `[${prev.itemRange.start}, ${prev.itemRange.end}] -> [${next.itemRange.start}, ${next.itemRange.end}]`);
        }

        if (warnings.length > 0) {
            console.warn(
                `[VirtualList] ⚠ after "${reason}":\n` + warnings.map(w => `  ${w}`).join('\n'),
                this.getStateChangeLog(10));
        }
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
                        if (this.state.pivots.some(pivot => pivot.itemKey === key)) {
                            // if the item is a pivot, we need to update its size and keep range
                            if (oldItem.range && newItem.size && newItem.size > 0)
                                oldItem.range = new NumberRange(oldItem.range.start, oldItem.range.start + newItem.size);
                        }
                        else
                            oldItem.range = undefined; // reset range
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
        let existingResizedCount = 0;
        let totalExistingSizeDiff = 0;
        let endAnchorHasChanged = false;
        const itemRefsWithWrongSize = new Array<HTMLElement>();
        for (const entry of entries) {
            const rect = entry.contentRect;
            const key = getItemKey(entry.target as HTMLElement);
            const rowGap = this.rowGap;
            const size = Math.ceil(rect.height + rowGap);
            if (!key) {
                notAnItem = true;
                if (entry.target === this.endAnchorRef) {
                    this.updateState('onResize: endAnchor', this.state, { endAnchorSize: size });
                    endAnchorHasChanged = true;
                }
                continue; // container or footer also can be resized
            }

            const item = this.items.get(key);
            if (item) {
                const itemRef = entry.target as HTMLElement;
                if (size == 0)
                    itemRefsWithWrongSize.push(itemRef);
                else {
                    const hasRemoved = this.unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                    const oldSize = item.size;
                    if (oldSize && oldSize > 0 && size > 0 && size != oldSize) {
                        existingResizedCount++;
                        itemsWereMeasured = true;
                        totalExistingSizeDiff += size - oldSize;
                    }
                    item.size = size;
                    if (this.state.pivots.some(pivot => pivot.itemKey === key)) {
                        // if the item is a pivot, we need to update its size and keep range
                        if (item.range)
                            item.range = new NumberRange(item.range.start, item.range.start + size);
                    }
                    else
                        item.range = undefined; // reset range

                    this.sizeCache.set(key, size);
                    this.statistics.addItem(item.size);
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
            // restore sticky end edge on item resize - not adding new one!
            if (!itemsWereMeasured && this.state.stickyEdge?.edge === this.defaultEdge)
                this.scrollToEdge(this.defaultEdge, false, 'non-item-resize');

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
            // Skip scroll position restoration if we have a scrollToKey and recently restored position
            if (this.state.renderState.scrollToKey && now - this.state.scrollPositionRestoredAt < ScrollDebounce)
                return;

            // Skip scroll position restoration if we are currently rendering - restoreScrollPosition() will be called
            // later in endRender() and will have up-to-date information about item sizes and positions
            if (this.state.renderStartedAt)
                return;

            // Skip scroll position restoration if we are recently completed rendering - endRender()
            // will have done it already, and we want to avoid doing it twice in a short time
            if (now - this.state.renderCompletedAt < ScrollDebounce)
                return;

            const renderState = { ...this.state.renderState, scrollToKey: undefined };
            const scrollMetadata = this.getScrollMetadata(renderState);

            // It is safe to avoid fastRaf and call restoreScrollPosition() directly as layout is already recalculated
            // at this point, and we are not in the middle of a render, so there will be no new information about
            // item sizes that could come in before the next paint
            void this.restoreScrollPosition(renderState, scrollMetadata, false);
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
            let hasStickyEdge = false;
            if (rs.hasVeryLastItem) {
                if (lastItemKey && this.visibleItems.has(lastItemKey)) {
                    this.setStickyEdge({ itemKey: lastItemKey, edge: VirtualListEdge.End });
                    hasStickyEdge = true;
                }
            }
            if (firstItemKey && !hasStickyEdge && rs.hasVeryFirstItem) {
                if (this.visibleItems.has(firstItemKey)) {
                    this.setStickyEdge({ itemKey: firstItemKey, edge: VirtualListEdge.Start });
                }
            }

            this.updateVisibleKeysThrottled();
        }
    };

    private onInteractiveEvent = (event: TouchEvent): void => {
        // Your touchend event handling logic here
        const itemRef = event.currentTarget;
        const key = getItemKey(itemRef as HTMLElement);
        if (!key)
            return;

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
        const msg = `[VirtualList:${this.identity}] ⚠ skeleton watchdog: spacers visible on screen for 2 checks`
            + ` (stateVersion=${version})`
            + `\n  startSpacer: h=${startRect.height.toFixed(0)} visible=${startVisible}`
            + `\n  endSpacer: h=${endRect.height.toFixed(0)} visible=${endVisible}`
            + `\n  viewport: [${viewRect.top.toFixed(0)}, ${viewRect.bottom.toFixed(0)}]`
            + `\n  scrollTop: ${this.ref.scrollTop}`
            + `\n  isRendering: ${this.isRendering}`;
        if (VirtualList.enableWatchdogFixes) {
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
            const timeSinceRestore = Date.now() - this.state.scrollPositionRestoredAt;
            if (timeSinceRestore > ScrollDebounce)
                this.setStickyEdge(null);
            else
                this.turnOffIsEndAnchorVisibleDebounced();
        }

        this.updateVisibleKeysThrottled();
    }

    private turnOnIsEndAnchorVisibleDebounced = debounce(() => this.turnOnIsEndAnchorVisible(), ScrollDebounce);

    private async turnOnIsEndAnchorVisible(): Promise<void> {
        // double-check visibility to prevent issues with scroll-to-the-last-item button
        await fastReadRaf();

        const isEndAnchorRefVisible = this.isItemPartiallyVisible(this.endAnchorRef);
        const isEndSpacerRefVisible = this.isItemPartiallyVisible(this.endSpacerRef)
            && this.endSpacerRef.getBoundingClientRect().height > VisibilityEpsilon;
        const isEndAnchorVisible = isEndAnchorRefVisible && !isEndSpacerRefVisible;
        if (!isEndAnchorVisible) {
            this.updateState('endAnchor: not visible', this.state, { isEndAnchorVisible: false });
            return;
        }

        this.updateState('endAnchor: on', this.state, { isEndAnchorVisible: true });
        if (this.state.renderState.hasVeryLastItem) {
            const edgeKey = this.getLastItemKey()!;
            this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.End });
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

            const scrollMetadata = this.getScrollMetadata(rs);

            // endRender is already being called from fastRaf, so useRaf = false
            await this.restoreScrollPosition(rs, scrollMetadata, false);
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

        // Schedule viewport update AFTER finalize — isRendering is now false.
        // The call inside restoreScrollPosition may get swallowed by the
        // leading-edge throttle while isRendering was still true.
        this.updateViewportThrottled();
    }

    private getScrollMetadata(rs: VirtualListRenderState): ScrollMetadata {
        const scrollToItemRef = this.getItemRef(rs.scrollToKey);
        let shouldUseSmoothScroll = false;
        let scrollType: ScrollToEdgeReason = 'unknown';
        let scrollFunc: (() => void) | undefined = undefined;

        if (scrollToItemRef != null) {
            // Server-side scroll request
            const isScrollToKeyVisible = this.isKeyVisible(rs.scrollToKey);
            if (!isScrollToKeyVisible) {
                if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    scrollType = 'last-item';
                    shouldUseSmoothScroll = this.state.stickyEdge?.edge == VirtualListEdge.End;
                    scrollFunc = () => {
                        this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, scrollType);
                        this.setStickyEdge({ itemKey: rs.scrollToKey!, edge: VirtualListEdge.End });
                    };
                } else {
                    const blockPosition: ScrollLogicalPosition = rs.scrollToKeyInTheMiddle
                        ? 'center'
                        : 'end'
                    scrollType = 'item';
                    scrollFunc = () => this.scrollTo(scrollToItemRef, false, blockPosition);
                }
            } else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                shouldUseSmoothScroll = true;
                scrollType = 'last-item';
                scrollFunc = () => {
                    this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, scrollType);
                    this.setStickyEdge({ itemKey: rs.scrollToKey!, edge: VirtualListEdge.End });
                };
            }
            else if (!rs.scrollToKeyInTheMiddle) {
                // Keep position of visible item
                scrollFunc = () => this.scrollTo(scrollToItemRef, false, 'end');
            }
        } else if (this.state.query.isNone && this.state.stickyEdge != null) {
            // Sticky edge scroll when we are not requesting data with query - render of new items only
            const itemKey = this.state.stickyEdge.edge === VirtualListEdge.Start && rs.hasVeryFirstItem
                ? this.getFirstItemKey()
                : this.state.stickyEdge.edge === VirtualListEdge.End && rs.hasVeryLastItem
                    ? this.getLastItemKey()
                    : null;
            if (itemKey) {
                shouldUseSmoothScroll = itemKey !== this.state.stickyEdge.itemKey;
                scrollType = 'sticky-edge';
                scrollFunc = () => {
                    this.setStickyEdge({ itemKey, edge: this.state.stickyEdge!.edge });
                    this.scrollToEdge(this.state.stickyEdge!.edge, shouldUseSmoothScroll, scrollType);
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
                scrollType = 'no-pivot';
                scrollFunc = () => this.scrollToEdge(this.defaultEdge, false, scrollType);
            }
        }

        return { shouldUseSmoothScroll: shouldUseSmoothScroll, scrollType, scroll: scrollFunc };
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

        const hasScheduled = await fastReadRaf(`updateViewport_${this.identity}`);
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

        const viewport = this.defaultEdge === VirtualListEdge.End
            ? new NumberRange(scrollTop - viewportHeight, scrollTop)
            : new NumberRange(scrollTop, scrollTop + viewportHeight);

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
        const isEndAnchorVisible = this.state.stickyEdge?.edge === VirtualListEdge.End;
        // debugLog?.log(`updateVisibleKeys: calling UpdateItemVisibility:`, visibleItems, isEndAnchorVisible);
        await this.blazorRef.invokeMethodAsync(
            'UpdateItemVisibility',
            this.identity,
            visibleItems,
            isEndAnchorVisible);
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
        newItem.shouldSkipKey = itemRef.dataset.skip === 'true';
        return newItem;
    }

    // Event handlers

    private onScroll = (ev: Event): void => {
        this.updateState('onScroll', this.state, { isScrolling: true });
        this.turnOffIsScrollingDebounced();

        if (this.isRendering)
            return;

        if (!ev.isTrusted)
            return; // Ignore non-user initiated scrolls

        // Ignore scroll events from programmatic scroll position restoration.
        // Setting scrollTop in restoreScrollPosition fires a trusted scroll event
        // that would otherwise be misidentified as user scroll, clearing pivots
        // and causing a visual jump.
        if (Date.now() - this.state.scrollPositionRestoredAt < ScrollRestoreGuard)
            return;

        // Clear sticky edge when user is scrolling via touch/pointer drag
        if (this.isPointerDown && this.state.stickyEdge != null && !this.state.isEndAnchorVisible) {
            // Require minimum displacement from edge before clearing — prevents
            // keyboard resize and small touch movements from losing sticky edge
            if (Math.abs(this.ref.scrollTop) > 50)
                this.setStickyEdge(null);
        }

        // Detect user scroll direction on the first trusted scroll event
        if (this.userScrollDirection === 'none') {
            const scrollTop = this.ref.scrollTop;
            const prevViewport = this.state.viewport ?? this.state.lastViewport;
            if (prevViewport) {
                const prevScrollTop = this.defaultEdge === VirtualListEdge.End
                    ? prevViewport.end
                    : prevViewport.start;
                if (scrollTop !== prevScrollTop) {
                    this.userScrollDirection = scrollTop < prevScrollTop ? 'up' : 'down';
                    warnLog?.log(`User scroll: +${this.userScrollDirection}`);
                }
            }
        }

        // Reset pivots on scroll
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

        const { stickyEdge, isEndAnchorVisible } = this.state;
        if (stickyEdge != null && !isEndAnchorVisible) {
            // Only clear if wheel direction is away from the sticky edge
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

    private updateCurrentPivots(interactiveKey?: string): void {
        if (this.isRendering)
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
            //     void this.restoreScrollPosition(this.state.renderState);
        }
    }

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce);

    private restoreScrollPositionOnResizeDebounced = debounce(() => {
        const renderState = { ...this.state.renderState, scrollToKey: undefined };
        const scrollMetadata = this.getScrollMetadata(renderState);
        void this.restoreScrollPosition(renderState, scrollMetadata, true);
    }, ScrollDebounce);

    private turnOffIsScrolling() {
        if (this.userScrollDirection !== 'none') {
            warnLog?.log(`User scroll: -${this.userScrollDirection}`);
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

    private getFirstItemRef(): HTMLElement | null {
        let ref = this.containerRef.firstElementChild?.nextElementSibling; // skip spacer
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
        let ref = this.containerRef.lastElementChild!.previousElementSibling; // skip end spacer
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

    private isItemFullyVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this.ref.getBoundingClientRect();
        return itemRect.top >= viewRect.top && itemRect.top <= viewRect.bottom
            && itemRect.bottom >= viewRect.top && itemRect.bottom <= viewRect.bottom
            && itemRect.height > 0;
    }

    private isItemPartiallyVisible(itemRef: HTMLElement): boolean {
        const itemRect = itemRef.getBoundingClientRect();
        const viewRect = this.ref.getBoundingClientRect();
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
        if (itemRef) {
            const authorBadge = itemRef.querySelector('div.c-author-badge');
            const navigateTarget = authorBadge ?? itemRef;
            navigateTarget.scrollIntoView({
                behavior: useSmoothScroll ? 'smooth' : 'auto',
                block: blockPosition,
                inline: 'nearest',
            });
        }
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
        fastRaf({
            read: () => {
                const isFarFromEdge = edge == VirtualListEdge.End
                    ? -this.ref.scrollTop > this.ref.offsetHeight
                    : this.ref.scrollTop > this.ref.offsetHeight;
                useSmoothScroll = useSmoothScroll && !isFarFromEdge;

                // Compute target scroll position based on layout direction
                if (this.defaultEdge === VirtualListEdge.End) {
                    // column-reverse: scrollTop=0 is end, negative is toward start
                    targetScrollTop = edge === VirtualListEdge.End
                        ? 0
                        : this.ref.clientHeight - this.ref.scrollHeight;
                } else {
                    // column: scrollTop=0 is start, positive is toward end
                    targetScrollTop = edge === VirtualListEdge.Start
                        ? 0
                        : this.ref.scrollHeight - this.ref.clientHeight;
                }
            },
            write: () => {
                if (useSmoothScroll) {
                    // Use scrollTo instead of scrollIntoView - more predictable on iOS
                    this.ref.scrollTo({
                        top: targetScrollTop,
                        behavior: 'smooth',
                    });
                }
                else {
                    this.ref.scrollTop = targetScrollTop;
                }
                if (edge == VirtualListEdge.End) {
                    void this.turnOnIsEndAnchorVisible();
                    this.turnOffIsEndAnchorVisibleDebounced.reset();
                }
                debugLog?.log('scrollToEdge: complete', edge, useSmoothScroll, reason);
            },
        });
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

    private async restoreScrollPosition(rs: VirtualListRenderState, scrollMetadata: ScrollMetadata | null = null, useRaf = false): Promise<void> {
        const { endAnchorSize } = this.state;
        const { hasUnmeasuredItems, defaultSpacerSize } = this;
        const result = new PromiseSource();
        // debugLog?.log(`restoreScrollPosition: start`);

        let scrollTop = 0;
        let scrollTopOffset = 0;
        let offset = 0;
        let totalSize = 0;
        let beforeSize = 0;
        let afterSize = 0;
        let spacerSize = 0;
        let endSpacerSize = 0;
        let totalSizeDiff = 0;
        const isInteractivePositioning = [...this.state.pivots].some(p => p.isInteractive)
            && scrollMetadata?.scrollType !== 'sticky-edge'
            && scrollMetadata?.scrollType !== 'last-item'
            && scrollMetadata?.scrollType !== 'item';

        // Cancel any pending viewport calculations
        this.updateViewportThrottled.reset();

        const options = {
            key: `restoreScrollPosition_${this.identity}`,
            read: () => {
                this._restoreScrollPositionPending = false;
                if (hasUnmeasuredItems)
                    this.measureItems();
                if (!this.state.itemRange)
                    this.ensureItemRangeCalculated();

                const { start, end, size: itemRangeSize } = this.state.itemRange ?? new NumberRange(0,0);
                const oldTotalSize = this.wrapperRef.offsetHeight;

                scrollTop = this.ref.scrollTop;

                if (rs.beforeCount !== null && rs.afterCount !== null) {
                    beforeSize = Math.floor(rs.beforeCount * this.statistics.itemSize);
                    afterSize =  Math.floor(rs.afterCount * this.statistics.itemSize);
                }
                else {
                    const knownRange = this.knownRange ?? new NumberRange(0,0);
                    const estimatedTotalSize = rs.estimatedCount
                        ? clamp(Math.floor(rs.estimatedCount * this.statistics.itemSize), knownRange.size, 5E6)
                        : 0;

                    let fullRange: NumberRange;
                    if (this.state.isStartKnown && this.state.isEndKnown)
                        fullRange = new NumberRange(knownRange.start, knownRange.end + endAnchorSize);
                    else if (this.state.isStartKnown) {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size + defaultSpacerSize);
                        fullRange = new NumberRange(knownRange.start, fullRangeSize);
                    }
                    else if (this.state.isEndKnown) {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size + defaultSpacerSize);
                        fullRange = new NumberRange(knownRange.end - fullRangeSize, knownRange.end);
                    }
                    else {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size + defaultSpacerSize * 2);
                        fullRange = this.defaultEdge === VirtualListEdge.End
                            ? new NumberRange(knownRange.end - fullRangeSize, knownRange.end)
                            : new NumberRange(knownRange.start, knownRange.start + fullRangeSize)
                    }

                    beforeSize = clamp(start - fullRange.start, 0, fullRange.size - itemRangeSize);
                    afterSize = clamp(fullRange.end - end, 0, fullRange.size - itemRangeSize);
                }
                if (beforeSize == 0 && !rs.hasVeryFirstItem)
                    beforeSize = defaultSpacerSize;
                if (afterSize == 0 && !rs.hasVeryLastItem)
                    afterSize = defaultSpacerSize;
                if (rs.hasVeryFirstItem)
                    beforeSize = 0;
                if (rs.hasVeryLastItem)
                    afterSize = 0;

                totalSize = itemRangeSize
                    + beforeSize
                    + afterSize
                    + endAnchorSize;

                if (!rs.hasVeryFirstItem && !rs.hasVeryLastItem)
                    totalSize = Math.max(totalSize, end, -start);
                else if (rs.hasVeryFirstItem)
                    totalSize = Math.max(totalSize, -start);
                else if (rs.hasVeryLastItem)
                    totalSize = Math.max(totalSize, end);

                totalSizeDiff = totalSize - oldTotalSize;

                if (this.defaultEdge === VirtualListEdge.End) {
                    offset = end;

                    if (offset > -endAnchorSize) {
                        // adjust item ranges
                        const resetDelta = this.resetItemRange();
                        if (resetDelta !== null) {
                            scrollTopOffset = resetDelta;
                            offset = this.state.itemRange!.end;
                        }
                    }
                    else if (rs.hasVeryLastItem && offset < -endAnchorSize) {
                        // reset if we are at the end anchor and offset is less than end anchor size - e.g., when item size is reduced
                        const resetDelta = this.resetItemRange();
                        if (resetDelta !== null) {
                            scrollTopOffset = resetDelta;
                            offset = this.state.itemRange!.end;
                        }
                    }

                    // Adjust spacer size
                    endSpacerSize = clamp(-offset - endAnchorSize, 0, defaultSpacerSize);
                    if (rs.hasVeryFirstItem) {
                        spacerSize = 0;
                    }
                    else {
                        spacerSize = clamp(oldTotalSize - itemRangeSize - endSpacerSize - endAnchorSize, 0, defaultSpacerSize);
                    }
                    offset += endSpacerSize; // adjust offset to include end spacer size
                }
                else {
                    offset = start;

                    if (offset < 0) {
                        // adjust item ranges
                        const resetDelta = this.resetItemRange();
                        if (resetDelta !== null) {
                            scrollTopOffset = resetDelta;
                            offset = this.state.itemRange!.start;
                        }
                    }
                    else if (rs.hasVeryFirstItem && offset > 0) {
                        // reset if we are at the start and the offset is greater than 0
                        const resetDelta = this.resetItemRange();
                        if (resetDelta !== null) {
                            scrollTopOffset = resetDelta;
                            offset = this.state.itemRange!.start;
                        }
                    }

                    // Adjust spacer size
                    spacerSize = clamp(offset, 0, defaultSpacerSize);
                    if (rs.hasVeryLastItem) {
                        endSpacerSize = 0;
                    }
                    else {
                        endSpacerSize = clamp(oldTotalSize - itemRangeSize - spacerSize - endAnchorSize, 0, defaultSpacerSize);
                    }
                    offset -= spacerSize; // adjust offset to include spacer size
                }
                // Set scrollPositionRestoredAt BEFORE write/scroll to guard onScroll from false "user scroll" detection
                this.updateState('scrollRestored', this.state, { scrollPositionRestoredAt: Date.now() });
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

                if (totalSizeDiff != 0 && this.state.isScrolling && rs.renderIndex > 0) {
                    // delay wrapper size increase when scrolling in Chromium to prevent issues with scroll position jumps
                    const setWrapperHeight = () => fastRaf({
                        write: () => {
                            if (this.state.isScrolling)
                                this.turnOffScrollingCallback = setWrapperHeight;
                            else {
                                this.wrapperRef.style.height = `${totalSize}px`;
                                // console.warn(
                                //     'restoreScrollPosition: wrapper size increased with DELAY!',
                                //     totalSize);
                            }
                        } });
                    this.turnOffScrollingCallback = setWrapperHeight;

                }
                else if (totalSizeDiff != 0) {
                    this.wrapperRef.style.height = `${totalSize}px`;
                }

                if (this.defaultEdge === VirtualListEdge.End) {
                    this.containerRef.style.bottom = `${-offset}px`;
                }
                else {
                    this.containerRef.style.top = `${offset}px`;
                }
                // Compensate scrollTop after item range reset to prevent visual jumps,
                // but skip when pinned to the bottom edge — staying pinned matters more
                const isPinnedToBottom = this.state.stickyEdge?.edge === VirtualListEdge.End
                    && this.defaultEdge === VirtualListEdge.End;
                if (scrollTopOffset && !isPinnedToBottom) {
                    this.ref.scrollTop = scrollTop + scrollTopOffset;
                }

                if (!isInteractivePositioning) {
                    scrollMetadata?.scroll?.();
                    debugLog?.log(`restoreScrollPosition: scroll set synchronously`, scrollMetadata?.scrollType);
                } else {
                    // Only clear sticky edge if user has actually scrolled away from the edge
                    if (this.state.stickyEdge && Math.abs(this.ref.scrollTop) > 50)
                        this.setStickyEdge(null);
                    debugLog?.log(`restoreScrollPosition: scroll set interactive`, scrollMetadata?.scrollType);
                }
                this.updateViewportThrottled();

                // debugLog?.log(`restoreScrollPosition: scroll set`, offset, totalSize, scrollTop, spacerSize, endSpacerSize);

                result.resolve(undefined);
            }
        };

        if (useRaf) {
            this._restoreScrollPositionPending = true;
            fastRaf(options);
        } else {
            options.read();
            options.write();
        }
        await result;
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

            if (size > 0) {
                item.size = size;
                if (this.state.pivots.some(pivot => pivot.itemKey === key)) {
                    // if the item is a pivot, we need to update its size and keep range
                    if (item.range)
                        item.range = new NumberRange(item.range.start, item.range.start + size);
                }
                else
                    item.range = undefined; // reset range
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

        const { renderState: rs, orderedItems, pivots } = this.state;
        const { visibleItems, defaultEdge, statistics } = this;

        // nothing to do when there are no items rendered
        if (orderedItems.length == 0)
            return false;

        let cornerstoneItemIndex = -1;
        let cornerstoneItem: VirtualListItem | null = null;
        const interactivePivots = pivots.filter(p => p.isInteractive);
        if (interactivePivots.length > 0) {
            // Use interactive pivot as cornerstone item
            const interactivePivot = interactivePivots[0];
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
            for (const cornerstoneItemKey of visibleItemKeys) {
                const index = orderedItems.findIndex(i => i.key === cornerstoneItemKey && i.range);
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

        const isCornerstoneRangeMissing = !cornerstoneItem?.range;
        const removedLastItem =
            this.defaultEdge === VirtualListEdge.End &&
            cornerstoneItemIndex === orderedItems.length - 1 &&
            rs.hasVeryLastItem &&
            (cornerstoneItem?.range?.end ?? 0) < -this.state.endAnchorSize;
        const removedFirstItem =
            this.defaultEdge === VirtualListEdge.Start &&
            cornerstoneItemIndex === 0 &&
            rs.hasVeryFirstItem &&
            (cornerstoneItem?.range?.start ?? 0) > 0;
        const needsRangeReset = isCornerstoneRangeMissing || removedLastItem || removedFirstItem;
        if (needsRangeReset) {
            // We have checked all items and there is no cornerstone item, so let's recalculate all ranges
            this.resetItemRange(true);
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

    private resetItemRange(canUseViewport = false): number | null {
        // This function is expected to be called with RAF
        const { orderedItems, endAnchorSize, renderState: rs } = this.state;
        const { defaultSpacerSize } = this;
        const fullRangeSize = this.knownRange?.size;

        let viewport = this.calculateViewport();
        if (viewport === null && this.state.viewport == null)
            return null; // viewport is not ready yet

        // Use current viewport if new one is not available
        if (viewport != null)
            this.updateState('resetItemRange: viewport', this.state, { viewport });
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

        if (this.defaultEdge === VirtualListEdge.End) {
            let cornerstoneItemIndex = orderedItems.length - 1;
            let cornerstoneItem = orderedItems[cornerstoneItemIndex];

            if (rs.beforeCount !== null && rs.afterCount !== null) {
                // We are able to calculate range based on before and after counts
                cornerstoneItem.range = new NumberRange(
                    0 - Math.floor(rs.afterCount * this.statistics.itemSize) - cornerstoneItem.size!,
                    0 - Math.floor(rs.afterCount * this.statistics.itemSize));
            }
            else if (canUseViewport && !rs.hasVeryLastItem) {
                // use coords of viewport and center ordered items
                const query = rs.query;
                const viewportCenter = viewport
                    ? viewport.start + viewport.size / 2
                    : query.isNone
                        ? 0 // We should not be here, but just in case
                        : query.virtualRange.start + query.virtualRange.size / 2;
                cornerstoneItemIndex = findCenterItemIndex();
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
                cornerstoneItem.range = new NumberRange(
                    Math.floor(viewportCenter - cornerstoneItem.size! / 2),
                    Math.ceil(viewportCenter + cornerstoneItem.size! / 2)
                );
            }
            else if (!rs.hasVeryLastItem) {
                // There is no query range and no very last item, so we have to calculate range manually with end spacer
                cornerstoneItem.range = new NumberRange(
                    0 - defaultSpacerSize - endAnchorSize - cornerstoneItem.size!,
                    0 - defaultSpacerSize - endAnchorSize);
            }
            else
                cornerstoneItem.range = new NumberRange(
                    0 - endAnchorSize - cornerstoneItem.size!,
                    0 - endAnchorSize);

            this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);

            rangeDelta = Math.max(...originalRanges.map((r, i) => orderedItems[i].range!.end - r.end));
            const newItemRange = new NumberRange(
                orderedItems[0].range!.start,
                orderedItems[orderedItems.length - 1].range!.end);
            if (fullRangeSize) {
                this.updateState('resetItemRange: End fullRange', this.state, {
                    itemRange: newItemRange,
                    minStart: 0 - fullRangeSize - endAnchorSize,
                    maxEnd: 0  - endAnchorSize,
                });
                // Do not reset isStartKnown \ isEndKnown as knownRange size has not changed
            }
            else {
                this.updateState('resetItemRange: End', this.state, {
                    itemRange: newItemRange,
                    minStart: orderedItems[0].range!.start,
                    maxEnd: orderedItems[orderedItems.length - 1].range!.end,
                    isEndKnown: rs.hasVeryLastItem,
                    isStartKnown: rs.hasVeryFirstItem,
                });
            }
            return rangeDelta;
        }
        else {
            let cornerstoneItemIndex = 0;
            let cornerstoneItem = orderedItems[cornerstoneItemIndex];

            if (rs.beforeCount !== null && rs.afterCount !== null) {
                // We are able to calculate range based on before and after counts
                cornerstoneItem.range = new NumberRange(
                    Math.floor(rs.beforeCount * this.statistics.itemSize),
                    Math.floor(rs.beforeCount * this.statistics.itemSize) + cornerstoneItem.size!);
            }
            else if (canUseViewport && !rs.hasVeryFirstItem) {
                // use coords of viewport and center ordered items
                const query = rs.query;
                const viewportCenter = viewport
                    ? viewport.start + viewport.size / 2
                    : query.isNone
                        ? 0 // We should not be here, but just in case
                        : query.virtualRange.start + query.virtualRange.size / 2;
                cornerstoneItemIndex = findCenterItemIndex();
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
                cornerstoneItem.range = new NumberRange(
                    Math.floor(viewportCenter - cornerstoneItem.size! / 2),
                    Math.ceil(viewportCenter + cornerstoneItem.size! / 2)
                );
            }
            else if (!rs.hasVeryFirstItem) {
                // There is no query range and no very first item, so we have to calculate range manually with spacer
                cornerstoneItem.range = new NumberRange(
                    defaultSpacerSize,
                    defaultSpacerSize + cornerstoneItem.size!);
            }
            else
                cornerstoneItem.range = new NumberRange(
                    0,
                    cornerstoneItem.size!);

            this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);
            rangeDelta = Math.max(...originalRanges.map((r, i) => orderedItems[i].range!.start - r.start));
            const newItemRange = new NumberRange(
                orderedItems[0].range!.start,
                orderedItems[orderedItems.length - 1].range!.end);
            if (fullRangeSize) {
                this.updateState('resetItemRange: Start fullRange', this.state, {
                    itemRange: newItemRange,
                    minStart: 0,
                    maxEnd: fullRangeSize + endAnchorSize,
                });
                // Do not reset isStartKnown \ isEndKnown as knownRange size has not changed
            }
            else {
                this.updateState('resetItemRange: Start', this.state, {
                    itemRange: newItemRange,
                    minStart: orderedItems[0].range!.start,
                    maxEnd: orderedItems[orderedItems.length - 1].range!.end,
                    isEndKnown: rs.hasVeryLastItem,
                    isStartKnown: rs.hasVeryFirstItem,
                });
            }
            return rangeDelta;
        }
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this.state.viewport)
            return;

        const query = this.getDataQuery();
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
        // debug helper
        // await delayAsync(1500);
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

    private getDataQuery(): VirtualListDataQuery {
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
            this.updateState('getDataQuery: invalidate', this.state, { itemRange: null });
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
}

// Helper functions
function getItemKey(itemRef: HTMLElement | null): string | null {
    return itemRef?.dataset?.key ?? null;
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
