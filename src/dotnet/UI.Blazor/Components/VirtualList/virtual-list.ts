import { debounce, PromiseSource, PromiseSourceWithTimeout, serialize, throttle } from 'promises';
import { NumberRange, Range } from './ts/range';
import { InertialScroll } from './ts/inertial-scroll';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { Pivot } from './ts/pivot';

import { Log } from 'logging';
import { fastRaf, fastReadRaf, fastWriteRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';
import { clamp } from 'math';

const { warnLog, debugLog } = Log.get('VirtualList');

const UpdateViewportInterval: number = 64;
const UpdateItemVisibilityInterval: number = 250;
const SafetyTimerPeriod: number = 1600;
const PivotSyncEpsilon: number = 16;
const VisibilityEpsilon: number = 4;
const EdgeEpsilon: number = 4;
const ScrollDebounce: number = 200;
const SkeletonDetectionBoundary: number = 200;
const MinViewPortSize: number = 400;
const RequestDataTimeout: number = 800;

type ScrollToEdgeReason = 'no-pivot' | 'last-item' | 'sticky-edge' | 'non-item-resize' | 'item-resize' | 'unknown';

export class VirtualList {
    /** ref to div.virtual-list */
    private readonly createdAt: number;
    private readonly ref: HTMLElement;
    private readonly containerRef: HTMLElement;
    private readonly renderStateRef: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly identity: string;
    private readonly defaultEdge: VirtualListEdge;
    private readonly defaultSpacerSize: number;
    private readonly expandTriggerMultiplier: number;
    private readonly expandMultiplier: number;
    private readonly spacerRef: HTMLElement;
    private readonly endSpacerRef: HTMLElement;
    private readonly renderIndexRef: HTMLElement;
    private readonly endAnchorRef: HTMLElement;
    private readonly layoutFooter?: HTMLElement;
    private readonly inertialScroll: InertialScroll;
    private readonly abortController: AbortController;
    private readonly itemSetChangeObserver: MutationObserver;
    private readonly sizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private readonly skeletonObserver0: IntersectionObserver;
    private readonly skeletonObserver1: IntersectionObserver;
    private readonly safetyTimerHandle: number;
    private readonly unmeasuredItems: Set<string>;
    private readonly visibleItems: Set<string>;
    private readonly items: Map<string, VirtualListItem>;
    private readonly statistics: VirtualListStatistics = new VirtualListStatistics();
    private readonly keySortCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

    private isDisposed = false;
    private cachedAllItemRefs: Array<HTMLLIElement> | null = null;
    private stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private whenRequestDataCompleted: PromiseSource<void> | null = null;
    private pivots: Pivot[] = [];
    private currentPivots: Pivot[] = [];
    private top: number;
    private windowScrollTop: number = 0;

    private renderStartedAt: number | null = null;
    private isNearSkeleton: boolean = false;
    private isEndAnchorVisible: boolean = false;
    private isScrolling: boolean = false;
    private scrollTime: number | null = null;
    private scrollDirection: 'up' | 'down' | 'none' = 'none';

    private query: VirtualListDataQuery = VirtualListDataQuery.None;
    private lastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private lastQueryTime: number | null = null;

    private renderState: VirtualListRenderState;
    private orderedItems: VirtualListItem[] = [];
    private itemRange: NumberRange | null = null;
    private viewport: NumberRange | null = null;
    private lastViewport: NumberRange | null = null;
    private spacerSize: number | null = null;
    private endSpacerSize: number | null = null;
    private shouldRecalculateItemRange: boolean = true;
    private shouldUpdateOrderedItems: boolean = true;

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        spacerSize: number,
        expandTriggerMultiplier: number,
        expandMultiplier: number,
    ) {
        return new VirtualList(
            ref,
            backendRef,
            identity,
            defaultEdge,
            spacerSize,
            expandTriggerMultiplier,
            expandMultiplier);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        defaultEdge: VirtualListEdge,
        spacerSize: number,
        expandTriggerMultiplier: number,
        expandMultiplier: number,
    ) {
        if (debugLog) {
            debugLog?.log(`constructor`);
            globalThis['virtualList'] = this;
        }

        this.createdAt = Date.now();
        this.ref = ref;
        this.blazorRef = backendRef;
        this.identity = identity;
        this.defaultEdge = defaultEdge;
        this.defaultSpacerSize = spacerSize;
        this.expandTriggerMultiplier = expandTriggerMultiplier;
        this.expandMultiplier = expandMultiplier;

        this.isDisposed = false;
        this.abortController = new AbortController();
        this.spacerRef = this.ref.querySelector(':scope > .c-spacer-start');
        this.endSpacerRef = this.ref.querySelector(':scope > .c-spacer-end');
        this.containerRef = this.ref.querySelector(':scope > .c-virtual-container');
        this.renderStateRef = this.ref.querySelector(':scope > .data.render-state');
        this.renderIndexRef = this.ref.querySelector(':scope > .data.render-index');
        this.endAnchorRef = this.ref.querySelector(':scope > .c-end-anchor');
        this.layoutFooter = document.querySelector('.layout-body-wrapper > .c-container > .layout-footer');
        this.inertialScroll = new InertialScroll(this.ref);

        // Events & observers
        const listenerOptions = { signal: this.abortController.signal };
        this.ref.addEventListener('scroll', this.onScroll, listenerOptions);
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

        this.safetyTimerHandle = self.setInterval(this.onSafetyTimer, SafetyTimerPeriod);

        this.unmeasuredItems = new Set<string>();
        this.visibleItems = new Set<string>();

        this.sizeObserver.observe(this.layoutFooter);
        this.visibilityObserver.observe(this.endAnchorRef);
        this.skeletonObserver0.observe(this.spacerRef);
        this.skeletonObserver0.observe(this.endSpacerRef);
        this.skeletonObserver1.observe(this.spacerRef);
        this.skeletonObserver1.observe(this.endSpacerRef);

        this.items = new Map<string, VirtualListItem>();
        this.renderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,
            keyRange: new Range<string>('', ''),
            beforeCount: null,
            afterCount: null,
            count: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
        };

        // set isRendering as soon as possible
        const origSetAttribute = this.renderIndexRef.setAttribute;
        this.renderIndexRef.setAttribute = (qualifiedName: string, value: string) => {
            // update pivots just before the render
            // we can do this because Blazor updates attributes before changing nodes
            // we SHOULD NOT fail there - otherwise Blazor will fail
            try {
                this.pivots = this.currentPivots;
                const time = Date.now();
                debugLog?.log(`renderStartedAt: `, time, value);
                this.renderStartedAt = time;
                origSetAttribute.call(this.renderIndexRef, qualifiedName, value);
            } catch (e) {
                warnLog?.log('renderIndex.setAttribute: failed', e);
            }
        };
        if (this.parseRenderState() === null)
            this.renderStartedAt = Date.now();

        if (this.containerRef.classList.contains('hide')) {
            this.containerRef.classList.remove('hide');
        }
        this.onItemSetChange([], this.itemSetChangeObserver);
    };

    /** Called by blazor */
    public dispose() {
        debugLog?.log(`dispose()`);
        this.isDisposed = true;
        this.abortController.abort();
        this.itemSetChangeObserver.disconnect();
        this.skeletonObserver0.disconnect();
        this.skeletonObserver1.disconnect();
        this.visibilityObserver.disconnect();
        this.sizeObserver.disconnect();
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
        clearInterval(this.safetyTimerHandle);
        this.ref.removeEventListener('scroll', this.onScroll);
    }

    /** Called by blazor */
    public reset() {
        debugLog?.log(`reset()`);
        this.lastViewport = null;
        this.viewport = null;
        this.lastQueryTime = null;
        this.stickyEdge = null;
        this.query = VirtualListDataQuery.None;
        this.lastQuery = VirtualListDataQuery.None;
        this.items.clear();
        this.orderedItems = [];
        this.pivots = [];
        this.renderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,
            keyRange: new Range<string>('', ''),
            beforeCount: null,
            afterCount: null,
            count: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
        };
    }

    private get isRendering(): boolean {
        return !!this.renderStartedAt;
    }

    private get isInitialRender(): boolean {
        const now = Date.now();
        // debugLog?.log('scrollToEdge: schedule', edge, useSmoothScroll, reason);
         // first 2.5 seconds after creating the virtual list
        return now - this.createdAt < 2500;
    }

    private get hasUnmeasuredItems(): boolean {
        return this.unmeasuredItems.size > 0 || !this.orderedItems;
    }

    private get fullRange(): NumberRange | null {
        return this.itemRange == null
            ? null
            : new NumberRange(
                this.itemRange.start - this.spacerSize ?? 0,
                this.itemRange.end + this.endSpacerSize ?? 0);
    }

    private parseRenderState(): VirtualListRenderState | null {
        try {
            const rsJson = this.renderStateRef.textContent;
            if (rsJson == null || rsJson === '')
                return null;

            const rs = JSON.parse(rsJson) as Required<VirtualListRenderState>;
            if (rs.renderIndex <= this.renderState.renderIndex)
                return null;

            const riText = this.renderIndexRef.dataset['renderIndex'];
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

    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    private onItemSetChange = (mutations: MutationRecord[], _observer: MutationObserver): void => {
        if (!this.isRendering) {
            if (mutations.length > 0)
                warnLog?.log('onItemSetChange: there are mutations, but isRendering() == false');
            this.renderStartedAt = Date.now();
        }
        const startedAt = this.renderStartedAt;
        if (debugLog) {
            const removedCount = mutations.reduce((prev, m) => prev + m.removedNodes.length, 0);
            const addedCount = mutations.reduce((prev, m) => prev + m.addedNodes.length, 0);
            const queryDuration = Math.max(0, startedAt - (this.lastQueryTime ?? startedAt));
            debugLog?.log(
                `onItemSetChange: query duration: `, queryDuration,
                '; added: ', addedCount,
                '; removed: ', removedCount,
                '; startedAt: ', startedAt);
        }

        // request recalculation of the item range and order item list as we've got new items
        this.cachedAllItemRefs = null;
        this.shouldRecalculateItemRange = true;
        this.shouldUpdateOrderedItems = true;

        // copy existing items - because we can remove them and add again at another tiles
        const oldItems = new Map<string, VirtualListItem>(this.items);
        for (const mutation of mutations) {
            if (mutation.type !== 'childList') { continue; }
            for (const node of mutation.removedNodes) {
                if (!node['dataset'])
                    continue;

                const itemRef = node as HTMLElement;
                const key = getItemKey(itemRef);
                this.items.delete(key);
                this.unmeasuredItems.delete(key);
                this.visibleItems.delete(key);
                this.sizeObserver.unobserve(itemRef);
                this.visibilityObserver.unobserve(itemRef);
            }
            for (const node of mutation.addedNodes) {
                const itemRef = node as HTMLElement;
                const key = getItemKey(itemRef);
                if (!key)
                    continue;

                const newItem = this.createListItem(key, itemRef);
                const oldItem = oldItems.get(key);
                if (oldItem) {
                    this.items.set(key, oldItem);
                    if (oldItem.size > 0)
                        this.unmeasuredItems.delete(key);
                } else
                    this.items.set(key, newItem);
            }
        }
        this.updateOrderedItems();
        void this.endRender();
    };

    private onResize = (entries: ResizeObserverEntry[], _observer: ResizeObserver): void => {
        let itemsWereMeasured = false;
        let notAnItem = false;
        const itemRefsWithWrongSize = new Array<HTMLElement>();
        for (const entry of entries) {
            const rect = entry.contentRect;
            const key = getItemKey(entry.target as HTMLElement);
            const size = rect.height;
            if (!key) {
                notAnItem = true;
                continue; // container or footer also can be resized
            }

            const item = this.items.get(key);
            if (item) {
                if (item.size < 0 && size == 0) {
                    itemRefsWithWrongSize.push(entry.target as HTMLElement);
                } else {
                    const hasRemoved = this.unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                    item.size = size;
                    this.statistics.addItem(item.size, item.countAs);
                }
            } else {
                const hasRemoved = this.unmeasuredItems.delete(key);
                itemsWereMeasured ||= hasRemoved;
            }
        }
        if (itemRefsWithWrongSize.length) {
            // ensure we have all sizes calculated
            fastRaf(() => {
                for (const itemRef of itemRefsWithWrongSize) {
                    const key = getItemKey(itemRef);
                    const item = this.items.get(key);
                    if (item && item.size < 0) {
                        const itemRect = itemRef.getBoundingClientRect();
                        item.size = itemRect.height;
                        this.statistics.addItem(item.size, item.countAs);
                    }
                    const hasRemoved = this.unmeasuredItems.delete(key);
                    itemsWereMeasured ||= hasRemoved;
                }

                if (this.unmeasuredItems.size == 0)
                    this.updateViewportThrottled();

                // recalculate item range as some elements were updated
                this.shouldRecalculateItemRange = itemsWereMeasured;
            });
        }
        if (notAnItem) {
            this.windowScrollTop = window.visualViewport.offsetTop;
            // restore sticky end edge on item resize - not adding new one!
            if (!itemsWereMeasured && this.stickyEdge?.edge === this.defaultEdge)
                this.scrollToEdge(this.defaultEdge, false, 'non-item-resize');

            if (DeviceInfo.isIos) {
                const htmlElement = document.getElementsByTagName('html')[0];
                const bodyElement = document.body;
                if (this.windowScrollTop == 0) {
                    htmlElement.style.position = 'static';
                    htmlElement.style.overflowX = null;
                    bodyElement.style.position = 'static';
                    bodyElement.style.overflowX = null;
                } else {
                    // Hack for iOS to keep text editor visible when virtual keyboard appears or new message is submitted
                    htmlElement.style.position = 'fixed';
                    htmlElement.style.overflowX = 'hidden';
                    bodyElement.style.position = 'fixed';
                    bodyElement.style.overflowX = 'hidden';
                }
            }
        } else if (!itemsWereMeasured && this.stickyEdge?.edge === this.defaultEdge)
            this.scrollToEdge(this.defaultEdge, true, 'item-resize');

        const lastItemWasMeasured = itemsWereMeasured && this.unmeasuredItems.size == 0;
        if (lastItemWasMeasured)
            this.updateViewportThrottled();

        // recalculate item range as some elements were updated
        this.shouldRecalculateItemRange = itemsWereMeasured;
    };

    private onItemVisibilityChange = (entries: IntersectionObserverEntry[], _observer: IntersectionObserver): void => {
        if (this.isRendering)
            return;

        let hasChanged = false;
        const rs = this.renderState;
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
                    } else if (this.isEndAnchorVisible) {
                        this.turnOffIsEndAnchorVisibleDebounced();
                        this.turnOnIsEndAnchorVisibleDebounced.reset();
                    }
                }
                continue;
            }
            if (!entry.isIntersecting) {
                hasChanged ||= this.visibleItems.has(key);
                this.visibleItems.delete(key);
            } else if ((entry.intersectionRatio >= 0.4 || entry.intersectionRect.height > MinViewPortSize / 2) && entry.isIntersecting) {
                hasChanged ||= !this.visibleItems.has(key);
                this.visibleItems.add(key);
            } else if (key === lastItemKey && entry.isIntersecting && rs.hasVeryLastItem && this.isEndAnchorVisible) {
                // the last item is bigger than viewport, but we see the end anchor - so let's mark it visible
                hasChanged ||= !this.visibleItems.has(key);
                this.visibleItems.add(key);
            }

            this.top = entry.rootBounds.top + VisibilityEpsilon;
        }
        if (hasChanged) {
            let hasStickyEdge = false;
            if (rs.hasVeryLastItem) {
                if (this.visibleItems.has(lastItemKey)) {
                    this.setStickyEdge({ itemKey: lastItemKey, edge: VirtualListEdge.End });
                    hasStickyEdge = true;
                }
            }
            if (!hasStickyEdge && rs.hasVeryFirstItem) {
                if (this.visibleItems.has(firstItemKey)) {
                    this.setStickyEdge({ itemKey: firstItemKey, edge: VirtualListEdge.Start });
                }
            }

            this.updateVisibleKeysThrottled();
        }
    };

    private updateVisibleItems(): void {
        const visibleItems = [...this.visibleItems];
        for (const itemKey of visibleItems) {
            const itemRef = this.getItemRef(itemKey);
            if (!itemRef) {
                this.visibleItems.delete(itemKey);
                continue;
            }

            const isItemVisible = this.isItemPartiallyVisible(itemRef);
            if (!isItemVisible)
                this.visibleItems.delete(itemKey);
        }
        if (this.visibleItems.size == 0) {
            const itemRefs = this.getAllItemRefs();
            // find visible items
            const visibilityStartIndex = binarySearch(itemRefs, itemRef => {
                const itemRect = itemRef.getBoundingClientRect();
                const viewRect = this.ref.getBoundingClientRect();
                return itemRect.bottom >= viewRect.top;
            });
            const visibilityEndIndex = binarySearch(itemRefs, itemRef => {
                const itemRect = itemRef.getBoundingClientRect();
                const viewRect = this.ref.getBoundingClientRect();
                return itemRect.top >= viewRect.bottom;
            });
            for (let i = visibilityStartIndex; i < visibilityEndIndex; i++) {
                const itemRef = itemRefs[i];
                const itemKey = getItemKey(itemRef);
                if (itemKey)
                    this.visibleItems.add(itemKey);
            }
        }
    }

    private onSkeletonVisibilityChange = (
        entries: IntersectionObserverEntry[],
        _observer: IntersectionObserver): void => {
        let isNearSkeleton = false;
        for (const entry of entries) {
            isNearSkeleton ||= entry.isIntersecting
                && entry.boundingClientRect.height > EdgeEpsilon;
        }
        if (isNearSkeleton) {
            this.isNearSkeleton = isNearSkeleton;
            // reset turn off attempt
            this.turnOffIsNearSkeletonDebounced.reset();
            this.updateViewportThrottled();
        } else
            this.turnOffIsNearSkeletonDebounced();
        // debug helper
        // console.warn("skeleton triggered", isNearSkeleton);
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce);

    private turnOffIsNearSkeleton(): void {
        this.isNearSkeleton = false;
        // debug helper
        // console.warn("skeleton os off");
    }

    private turnOffIsEndAnchorVisibleDebounced = debounce(() => this.turnOffIsEndAnchorVisible(), ScrollDebounce);

    private turnOffIsEndAnchorVisible(): void {
        this.isEndAnchorVisible = false;
        if (this.stickyEdge?.edge === VirtualListEdge.End) {
            this.setStickyEdge(null);
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
            this.isEndAnchorVisible = false;
            return;
        }

        this.isEndAnchorVisible = true;
        if (this.renderState.hasVeryLastItem) {
            const edgeKey = this.getLastItemKey();
            this.setStickyEdge({ itemKey: edgeKey, edge: VirtualListEdge.End });
        }
        this.updateVisibleKeysThrottled();
    }

    private async endRender(): Promise<void> {
        if (!this.isRendering) {
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;
            return;
        }
        const rs = this.parseRenderState();
        if (rs === null) {
            this.renderStartedAt = null;
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;
            return;
        }

        if (rs.query.isNone) {
            // Reset query - it become irrelevant after render without query
            this.query = VirtualListDataQuery.None;
            this.lastQuery = VirtualListDataQuery.None;
        }

        this.renderState = rs;
        let spacerSize = this.defaultSpacerSize;
        let endSpacerSize = this.defaultSpacerSize;
        if (rs.beforeCount !== null && rs.afterCount !== null) {
            spacerSize = rs.beforeCount * Math.floor(this.statistics.itemSize);
            endSpacerSize = rs.afterCount * Math.floor(this.statistics.itemSize);
        } else if (!rs.keyRange?.start) {
            if (rs.renderIndex <= 2) {
                // no data loaded yet
                spacerSize = 1000;
                endSpacerSize = 0;
            } else {
                // empty result list
                spacerSize = 0;
                endSpacerSize = 0;
            }
        } else {
            if (rs.hasVeryFirstItem)
                spacerSize = 0;
            if (rs.hasVeryLastItem)
                endSpacerSize = 0;
        }

        // Unable to delay until the next frame - will lead to scroll jumps
        this.spacerRef.style.height = `${spacerSize}px`;
        this.endSpacerRef.style.height = `${endSpacerSize}px`;
        this.spacerSize = spacerSize;
        this.endSpacerSize = endSpacerSize;

        const startedAt = this.renderStartedAt;
        const now = Date.now();
        debugLog?.log(`endRender, renderIndex = #${rs.renderIndex}, duration = ${now - startedAt}ms, rs =`, rs);
        let positionSet = false;
        if (this.pivots.length && rs.scrollToKey == null) {
            // Restore scroll position first, and then use smooth scroll to go to the scroll target
            positionSet = this.restoreScrollPosition(startedAt, !rs.query.isNone);
        }

        try {
            // Update statistics
            if (!rs.query.isNone && rs.query.expectedCount)
                this.statistics.addResponse(rs.count, rs.query.expectedCount);

            const scrollToItemRef = this.getItemRef(rs.scrollToKey);
            if (scrollToItemRef != null) {
                // Server-side scroll request
                if (!this.isKeyVisible(rs.scrollToKey)) {
                    if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                        if (this.stickyEdge?.edge == VirtualListEdge.End)
                            this.scrollToEdge(VirtualListEdge.End, true, 'last-item');
                        else
                            this.scrollToEdge(VirtualListEdge.End, false, 'last-item');
                        this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                    } else {
                        this.scrollTo(scrollToItemRef, false);
                    }
                } else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                    this.scrollToEdge(VirtualListEdge.End, true, 'last-item');
                }
            } else if (this.stickyEdge != null) {
                // Sticky edge scroll when we are not requesting data with query - render of new items only
                const itemKey = this.stickyEdge?.edge === VirtualListEdge.Start && rs.hasVeryFirstItem
                    ? this.getFirstItemKey()
                    : this.stickyEdge?.edge === VirtualListEdge.End && rs.hasVeryLastItem
                        ? this.getLastItemKey()
                        : null;
                if (itemKey == null) {
                    console.warn('endRender: sticky edge scroll failed', this.stickyEdge);
                    // let's scroll to the latest edge key when we've got a lot of new messages
                    if (this.stickyEdge?.edge === VirtualListEdge.End) {
                        let itemRef = this.getItemRef(this.stickyEdge.itemKey);
                        this.scrollTo(itemRef, false);
                    }
                    this.setStickyEdge(null);
                } else {
                    this.setStickyEdge({ itemKey: itemKey, edge: this.stickyEdge.edge });
                    if (this.stickyEdge?.edge === VirtualListEdge.End) {
                        this.scrollToEdge(VirtualListEdge.End, true, 'sticky-edge');
                    } else if (this.stickyEdge?.edge === VirtualListEdge.Start) {
                        this.scrollToEdge(VirtualListEdge.Start, true, 'sticky-edge');
                    }
                }
            } else if (!positionSet) {
                if (rs.renderIndex <= 2)
                    this.scrollToEdge(this.defaultEdge, false, 'no-pivot');
            }

            // ensure scroll position and size are recalculated
            await fastWriteRaf();
        } finally {
            this.renderStartedAt = null;
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;

            this.lastViewport = this.viewport;
            this.pivots = [];
            this.itemRange = null;
            this.viewport = null;

            let anchorRefs: HTMLLIElement[] = [];
            fastRaf({
                read: () => {
                    anchorRefs = [...this.containerRef.querySelectorAll<HTMLLIElement>(':scope > li.item.anchor')];
                }, write: () => {
                    for (const anchorRef of anchorRefs) {
                        // remove native anchor after restoring position
                        anchorRef.classList.remove('anchor');
                    }

                    // Schedule update of the current pivots after the render
                    this.scheduleUpdateCurrentPivots();
                },
            });
        }
    }

    private readonly updateViewportThrottled = throttle(
        this.updateViewport,
        UpdateViewportInterval,
        'default',
        'updateViewport');

    private async updateViewport(): Promise<void> {
        const rs = this.renderState;
        if (this.isDisposed || this.isRendering)
            return;

        // do not update client state when we haven't completed rendering for the first time
        if (rs.renderIndex === -1)
            return;

        await fastReadRaf();

        let viewport: NumberRange | null = null;
        if (this.fullRange) {
            const anchorHeight = this.endAnchorRef.getBoundingClientRect().height;
            const viewportHeight = this.ref.clientHeight - anchorHeight;
            const scrollTop = this.ref.scrollTop;
            const clientViewport = new NumberRange(scrollTop, scrollTop + viewportHeight);
            const fullRange = this.fullRange;
            if (fullRange != null) {
                viewport = clientViewport.fitInto(fullRange);
            }
        }
        // set min viewport size if smaller
        if (viewport && viewport.size < MinViewPortSize)
            viewport = new NumberRange(viewport.end - MinViewPortSize, viewport.end);

        // update item range
        const isViewportUnknown = viewport == null;
        if (!this.ensureItemRangeCalculated() && !this.itemRange) {
            this.updateViewportThrottled();
        } else if (isViewportUnknown)
            await this.updateViewport();
        else {
            if (this.viewport && viewport) {
                if (viewport.start < this.viewport.start)
                    this.scrollDirection = 'up';
                else
                    this.scrollDirection = 'down';
            }

            this.viewport = viewport;
            await this.requestData();
        }
    }

    private readonly updateVisibleKeysThrottled = throttle(
        () => this.updateVisibleKeys(),
        UpdateItemVisibilityInterval,
        'delayHead',
        'updateVisibleKeys');
    private readonly updateVisibleKeys = serialize(async () => {
        if (this.isDisposed || !this.renderState.keyRange.start)
            return;

        await fastReadRaf();
        this.updateVisibleItems();
        const visibleItems = [...this.visibleItems].sort(this.keySortCollator.compare);
        const isEndAnchorVisible = this.stickyEdge?.edge === VirtualListEdge.End;
        debugLog?.log(`updateVisibleKeys: calling UpdateItemVisibility:`, visibleItems, isEndAnchorVisible);
        await this.blazorRef.invokeMethodAsync(
            'UpdateItemVisibility',
            this.identity,
            visibleItems,
            isEndAnchorVisible);
    }, 2);

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
        this.orderedItems = orderedItems;
        this.shouldUpdateOrderedItems = false;
    }

    private createListItem(itemKey: string, itemRef: HTMLElement): VirtualListItem {
        const countAs = getItemCountAs(itemRef);
        const newItem = new VirtualListItem(itemKey, countAs ?? 1);
        this.unmeasuredItems.add(itemKey);
        this.sizeObserver.observe(itemRef, { box: 'border-box' });
        this.visibilityObserver.observe(itemRef);
        return newItem;
    }

    // Event handlers

    private onSafetyTimer = (): void => {
        const pendingItemSetChanges = this.itemSetChangeObserver.takeRecords();
        if (pendingItemSetChanges.length > 0) {
            warnLog?.log(`onSafetyTimer: pending item set changes found`);
            this.onItemSetChange(pendingItemSetChanges, this.itemSetChangeObserver);
        }
        const pendingVisibilityChanges = this.visibilityObserver.takeRecords();
        if (pendingVisibilityChanges.length > 0) {
            warnLog?.log(`onSafetyTimer: pending visibility changes found`);
            this.onItemVisibilityChange(pendingVisibilityChanges, this.visibilityObserver);
        }
        if (this.isNearSkeleton)
            this.updateViewportThrottled();
    };

    private onScroll = (ev: Event): void => {
        this.isScrolling = true;
        this.turnOffIsScrollingDebounced();

        // large messages is being displayed and probably can have outdated pivot offset
        // let's update offset
        if (this.isRendering)
            return;

        this.updateViewportThrottled();
        this.scheduleUpdateCurrentPivots();
    };

    private scheduleUpdateCurrentPivots(): void {
        if (this.isDisposed)
            return;

        fastRaf(() => this.updateCurrentPivots());
    }

    private updateCurrentPivots(): void {
        if (this.isRendering)
            return;

        const time = Date.now();
        const pivots = new Array<Pivot>();
        const pivotRefs = new Array<HTMLElement>();
        // add query edges and second\last items as pivots

        // do not use first item as pivot - it might be changed during rendering of items above - e.g. author circle might disappear
        const firstItemRef = this.getFirstItemRef();
        const firstItemKey = getItemKey(firstItemRef);
        const secondItemRef = firstItemRef?.nextElementSibling as HTMLElement;
        const secondItemKey = getItemKey(secondItemRef);
        let medianVisibleKey = null;
        if (this.visibleItems.size) {
            const visibleItems = [...this.visibleItems.values()];
            medianVisibleKey = visibleItems[Math.floor(visibleItems.length / 2)];
        //     const medianRef = this.getItemRef(medianVisibleKey);
        //     if (medianRef)
        //         if (!medianRef.classList.contains('anchor'))
        //             medianRef.classList.add('anchor');
        }

        const itemKeys = [medianVisibleKey, this.getLastItemKey(), this.query.keyRange?.end, secondItemKey, this.query.keyRange?.start];
        for (let itemKey of itemKeys) {
            if (itemKey === firstItemKey)
                continue;

            const pivotRef = this.getItemRef(itemKey);
            if (!pivotRef)
                continue;

            pivotRefs.push(pivotRef);
            // measure scroll position
            const itemRect = pivotRef.getBoundingClientRect();
            const pivot: Pivot = {
                itemKey,
                offset: Math.round(itemRect.top),
                time,
            };
            pivots.push(pivot);
        }
        this.currentPivots = pivots;

        const whenRequestDataCompleted = this.whenRequestDataCompleted;
        if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted() && !this.isRendering) {
            this.scheduleUpdateCurrentPivots();
        }
    }

    private turnOffIsScrollingDebounced = debounce(() => this.turnOffIsScrolling(), ScrollDebounce);

    private turnOffIsScrolling() {
        this.isScrolling = false;
        this.scrollDirection = 'none';

        // this line below can fix rendering artifacts when some entries are blank
        // but adds significant stutter during scroll
        // this.forceRepaintThrottled();

        if (this.isRendering || this.isDisposed)
            return;

        this.updateViewportThrottled();
    }

    private getAllItemRefs(): HTMLLIElement[] {
        if (this.cachedAllItemRefs === null) {
            const elementRefs = this.containerRef.children as HTMLCollectionOf<HTMLLIElement>;
            this.cachedAllItemRefs = Array.from(elementRefs);
        }
        return this.cachedAllItemRefs;
    }

    private getItemRef(key: string): HTMLElement | null {
        if (key == null)
            return null;

        // return this._containerRef.querySelector(`:scope > .item[data-key="${key}"]`);
        return document.getElementById(key);
    }

    private getFirstItemRef(): HTMLElement | null {
        const itemRef = this.containerRef.firstElementChild;
        if (itemRef == null || !itemRef.classList.contains('item'))
            return null;
        return itemRef as HTMLElement;
    }

    private getFirstItemKey(): string | null {
        return getItemKey(this.getFirstItemRef());
    }

    private getLastItemRef(): HTMLElement | null {
        const itemRef = this.containerRef.lastElementChild;
        if (itemRef == null || !itemRef.classList.contains('item'))
            return null;
        return itemRef as HTMLElement;
    }

    private getLastItemKey(): string | null {
        return getItemKey(this.getLastItemRef());
    }

    private isKeyVisible(itemKey: string): boolean {
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

    private forceReflow(): void {
        this.ref.style.display = 'none';
        void this.ref.offsetWidth;
        this.ref.style.display = '';
    }

    private scrollTo(
        itemRef?: HTMLElement,
        useSmoothScroll: boolean = false,
        blockPosition: ScrollLogicalPosition = 'center') {
        debugLog?.log(`scrollTo, item key:`, getItemKey(itemRef));
        this.scrollTime = Date.now();
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

    private scrollToEdge(edge: VirtualListEdge = VirtualListEdge.End, useSmoothScroll: boolean = false, reason: ScrollToEdgeReason = "unknown"): void {

        // debugLog?.log('scrollToEdge: schedule', edge, useSmoothScroll, reason);

        const isInitialRender = this.isInitialRender;
        if (isInitialRender && (reason === 'non-item-resize' || reason === 'item-resize'))
            return; // do not scroll to the end on initial render on spacer resize

        if (this.renderState.renderIndex <= 2 || isInitialRender)
            useSmoothScroll = false; // fix for scroll to the end on chat switch
        this.scrollTime = Date.now();
        let scrollHeight = 0;
        fastRaf({
            read: () => {
                scrollHeight = this.ref.scrollHeight;
                const isFarFromEdge = edge == VirtualListEdge.End
                    ? (scrollHeight - this.ref.scrollTop) > 2 * this.ref.offsetHeight
                    : this.ref.scrollTop > this.ref.offsetHeight;
                useSmoothScroll = useSmoothScroll && !isFarFromEdge;
            },
            write: () => {
                const target = edge == VirtualListEdge.End
                    ? this.endAnchorRef
                    : this.spacerRef;
                if (useSmoothScroll)
                    target.scrollIntoView({
                        behavior: 'smooth',
                        block: 'center',
                        inline: 'nearest',
                    });
                else {
                    this.ref.scrollTop = edge == VirtualListEdge.End ? scrollHeight : 0;
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

        const old = this.stickyEdge;
        if (old?.itemKey !== stickyEdge?.itemKey || old?.edge !== stickyEdge?.edge) {
            debugLog?.log(`setStickyEdge:`, stickyEdge);
            this.stickyEdge = stickyEdge;
            if (stickyEdge?.edge === VirtualListEdge.End) {
                const lastItemRef = this.getLastItemRef();
                if (!lastItemRef)
                    return false;

                let hasAnchor = false;
                fastRaf({
                    read: () => { hasAnchor = lastItemRef.classList.contains('anchor'); },
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

    private restoreScrollPosition(renderTime: number, useRaf: boolean = true): boolean {
        const pivots = [...this.pivots];
        const tuple = pivots
            .map(pivot => ({ pivotRef: this.getItemRef(pivot.itemKey), pivot }))
            .find(t => t.pivotRef);
        if (!tuple) {
            warnLog?.log(`restoreScrollPosition: there are no pivot refs found!`);
            return false;
        }

        // resync scroll to make pivot ref position the same within viewport
        const { pivotRef, pivot } = tuple;
        let scrollTop: number | null = null;
        let shouldResync = false;

        const options = {
            read: () => {
                const pivotEpsilon = PivotSyncEpsilon;
                // code below triggers forced reflow - but it's OK  - reflow will be triggered after adding new elements anyway
                const pivotOffset = pivot.offset;
                const itemRect = pivotRef.getBoundingClientRect();
                const currentPivotOffset = Math.round(itemRect.top);
                const dPivotOffset = pivotOffset - currentPivotOffset;
                scrollTop = this.ref.scrollTop;
                if (Math.abs(dPivotOffset) > pivotEpsilon) {
                    debugLog?.log(`restoreScrollPosition: [${pivot.itemKey}]: ~${scrollTop} = ${pivotOffset} ~> ${Math.round(
                        itemRect.top)} + ${dPivotOffset}`, pivot, useRaf);
                    scrollTop -= dPivotOffset;
                    shouldResync = true;
                }
            },
            write: () => {
                debugLog?.log(`restoreScrollPosition: pivots`, pivots, renderTime);

                if (shouldResync) {
                    // debug helper
                    // pivotRef.style.backgroundColor = `rgb(${Math.random() * 255},${Math.random() * 255},${Math.random() * 255})`;

                    // set scroll styles to improve UX on iOS before setting scrollTop
                    this.inertialScroll.freeze();
                    this.ref.scrollTop = scrollTop;
                    fastRaf({
                        write: () => {
                            this.inertialScroll.unfreeze();
                        },
                    });
                    debugLog?.log(`restoreScrollPosition: scroll set`, scrollTop);
                } else if (this.isNearSkeleton && Math.abs(scrollTop) < PivotSyncEpsilon) {
                    debugLog?.log(`restoreScrollPosition: scrollTop ~= 0`, this.isRendering);

                    // we have lost scroll offset so let's scroll to the last visible pivot
                    this.scrollTo(pivotRef, false);
                } else
                    debugLog?.log(`restoreScrollPosition: skipped [${pivot.itemKey}]: ~${scrollTop}`, pivot);
            },
        };
        if (useRaf)
            fastRaf(options);
        else {
            options.read();
            options.write();
        }
        return true;
    }

    private async measureItems(): Promise<void> {
        if (!this.hasUnmeasuredItems)
            return;

        await fastReadRaf();
        const unmeasuredItems = [...this.unmeasuredItems];
        let itemsWereMeasured = false;
        for (const key of unmeasuredItems) {
            const item = this.items.get(key);
            if (item && item.size < 0) {
                const itemRef = this.getItemRef(key);
                if (itemRef) {
                    const itemRect = itemRef.getBoundingClientRect();
                    item.size = itemRect.height;
                } else
                    this.items.delete(key);
            }
            const hasRemoved = this.unmeasuredItems.delete(key);
            itemsWereMeasured ||= hasRemoved;
        }

        // recalculate item range as some elements were updated
        this.shouldRecalculateItemRange = itemsWereMeasured;
    }

    private ensureItemRangeCalculated(): boolean {
        // nothing to do when unmeasured items still exist or there were no new renders
        if (this.hasUnmeasuredItems) {
            void this.measureItems();
            return false;
        }

        if (this.shouldUpdateOrderedItems)
            this.updateOrderedItems();

        if (!this.shouldRecalculateItemRange && this.itemRange)
            return false;

        // nothing to do when there are no items rendered
        if (this.orderedItems.length == 0)
            return false;

        const orderedItems = this.orderedItems;
        const itemOrder = new Map<string, number>();
        const viewport = this.viewport || this.lastViewport;
        const visibleItems = this.visibleItems;
        let cornerStoneItemIndex = 0;
        let cornerStoneItem = orderedItems[0];

        for (let i = 0; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            itemOrder.set(item.key, i);
        }

        if (this.defaultEdge === VirtualListEdge.End) {
            // find rightmost measured item if the default edge is `End`
            cornerStoneItemIndex = orderedItems.length - 1;
            cornerStoneItem = orderedItems[cornerStoneItemIndex];
            while (cornerStoneItemIndex > 0 && !cornerStoneItem.isMeasured)
                cornerStoneItem = orderedItems[--cornerStoneItemIndex];

            if (!cornerStoneItem.range) {
                if (viewport && visibleItems.size > 0) {
                    // use last visible item as cornerstone
                    const lastItem = [...visibleItems]
                        .map(it => itemOrder.get(it))
                        .map(i => ({ i: i, item: orderedItems[i] }))
                        .reduce((a, b) => (a && a.i > b.i) ? a : b);
                    if (lastItem?.item) {
                        cornerStoneItemIndex = lastItem.i;
                        cornerStoneItem = lastItem.item;
                        cornerStoneItem.range = new NumberRange(
                            viewport.end - cornerStoneItem.size,
                            viewport.end);
                    }
                }
            }
            if (!cornerStoneItem.range) {
                // reset ranges and recalculate from cornerstone item
                cornerStoneItemIndex = orderedItems.length - 1;
                cornerStoneItem = orderedItems[cornerStoneItemIndex];
                // try to reuse coords of previously rendered items
                if (!this.lastQuery.isNone) {
                    const { virtualRange } = this.lastQuery;
                    cornerStoneItem.range = new NumberRange(
                        virtualRange.end - cornerStoneItem.size,
                        virtualRange.end);
                } else
                    cornerStoneItem.range = new NumberRange(-cornerStoneItem.size, 0);
            }
        } else {
            // find leftmost measured item if the default edge is `Start`
            while (cornerStoneItemIndex < orderedItems.length - 1 && !cornerStoneItem.isMeasured)
                cornerStoneItem = orderedItems[++cornerStoneItemIndex];

            if (!cornerStoneItem.range) {
                if (viewport && visibleItems.size > 0) {
                    // use first visible item as cornerstone
                    const firstItem = [...visibleItems]
                        .map(it => itemOrder.get(it))
                        .map(i => ({ i: i, item: orderedItems[i] }))
                        .reduce((a, b) => (a && a.i > b.i) ? b : a);
                    if (firstItem?.item) {
                        cornerStoneItemIndex = firstItem.i;
                        cornerStoneItem = firstItem.item;
                        cornerStoneItem.range = new NumberRange(
                            viewport.start,
                            viewport.start + cornerStoneItem.size);
                    }
                }
            }
            if (!cornerStoneItem.range) {
                // reset ranges and recalculate from cornerstone item
                cornerStoneItemIndex = 0;
                cornerStoneItem = orderedItems[cornerStoneItemIndex];
                // try to reuse coords of previously rendered items
                if (!this.lastQuery.isNone) {
                    const { virtualRange } = this.lastQuery;
                    cornerStoneItem.range = new NumberRange(
                        virtualRange.start,
                        virtualRange.start + cornerStoneItem.size);
                } else
                    cornerStoneItem.range = new NumberRange(0, cornerStoneItem.size);
            }
        }

        // calculate range of other items
        let prevItem = cornerStoneItem;
        for (let i = cornerStoneItemIndex + 1; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.end, prevItem.range.end + item.size);
            prevItem = item;
        }
        prevItem = cornerStoneItem;
        for (let i = cornerStoneItemIndex - 1; i >= 0; i--) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.start - item.size, prevItem.range.start);
            prevItem = item;
        }

        this.itemRange = new NumberRange(
            orderedItems[0].range.start,
            orderedItems[orderedItems.length - 1].range.end);

        this.shouldRecalculateItemRange = false;
        return true;
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this.viewport || !this.itemRange)
            return;

        const query = this.getDataQuery();
        if (!this.mustRequestData(query)) {
            // debugLog?.log(`requestData: request is unnecessary`);
            return;
        }
        if (query.isNone)
            return;

        this.query = query;

        const whenRequestDataCompleted = this.whenRequestDataCompleted;
        if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted()) {
            debugLog?.log(`requestData: the previous request is not completed yet`);
            return;
        }

        const newWhenRequestDataCompleted = new PromiseSourceWithTimeout<void>();
        newWhenRequestDataCompleted.setTimeout(RequestDataTimeout, () => {
            newWhenRequestDataCompleted.resolve(undefined);
        });
        this.whenRequestDataCompleted = newWhenRequestDataCompleted;

        debugLog?.log(`requestData: query:`, this.query, this.viewport, this.viewport?.size);
        this.lastQueryTime = Date.now();
        // debug helper
        // await delayAsync(150);
        this.scheduleUpdateCurrentPivots();
        await this.blazorRef.invokeMethodAsync('RequestData', this.query);
        this.lastQuery = this.query;
    }

    private mustRequestData(query: VirtualListDataQuery): boolean {
        const itemRange = this.itemRange;
        const queryRange = query.virtualRange;
        const viewport = this.viewport;
        const rs = this.renderState;
        if (!itemRange || !queryRange)
            return false;

        if (!viewport)
            return false;

        if (itemRange.size == 0)
            return true; // re-request data with empty query

        if (this.query === query)
            return false;

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
        const rs = this.renderState;
        const itemSize = this.statistics.itemSize;
        const responseFulfillmentRatio = rs.beforeCount !== null && rs.afterCount !== null
            ? 1 // We know count precisely
            : this.statistics.responseFulfillmentRatio;
        const viewport = this.viewport;
        const alreadyLoaded = this.itemRange;
        if (!viewport || !alreadyLoaded)
            return this.lastQuery;

        if (this.hasUnmeasuredItems) { // Let's wait for measurement to complete first
            void this.measureItems();
            return this.lastQuery;
        }
        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return this.lastQuery; // We have already loaded all data

        if (this.isRendering)
            return this.lastQuery; // Do not request data during rendering as it might be inconsistent

        const viewportSize = viewport.size;
        const lastQuerySide = this.lastQuery.moveRange.size === 0
            ? 'none'
            : this.lastQuery.moveRange.start >= 0 && this.lastQuery.moveRange.end >= 0
                ? 'end'
                : 'start';
        const alreadyLoadedFromStart = Math.abs(alreadyLoaded.start - viewport.start);
        const alreadyLoadedTillEnd = Math.abs(alreadyLoaded.end - viewport.end);
        const loadZoneTrigger = viewportSize * this.expandTriggerMultiplier;
        // keep at least _expandMultiplier * viewport more in both directions
        const loadZoneSize = viewportSize * this.expandMultiplier;
        let loadStart = viewport.start - loadZoneSize;
        let loadEnd = viewport.end + loadZoneSize;

        switch (lastQuerySide) {
            case 'none':
                break;
            case 'end':
                // check whether we need to continue loading from the end
                if (alreadyLoadedTillEnd < loadZoneTrigger) {
                    if (!rs.hasVeryLastItem && (rs.afterCount === null || rs.afterCount > 5)) {
                        loadEnd = viewport.end + loadZoneSize * 1.5;
                        loadStart = viewport.start - viewportSize / 3;
                    }
                } else if (alreadyLoadedFromStart < viewportSize / 3) { // smaller than half of viewport to change load direction
                    if (!rs.hasVeryFirstItem)
                        loadStart = viewport.start - loadZoneSize;
                    else
                        return this.lastQuery;
                }
                break;
            case 'start':
                // check whether we need to continue loading from the start
                if (alreadyLoadedFromStart < loadZoneTrigger) {
                    if (!rs.hasVeryFirstItem && (rs.beforeCount === null || rs.beforeCount > 5)) {
                        loadStart = viewport.start - loadZoneSize * 1.5;
                        loadEnd = viewport.end + viewportSize / 3;
                    }
                } else if (alreadyLoadedTillEnd < viewportSize / 3) { // smaller than 1/3 of viewport to change load direction
                    if (!rs.hasVeryLastItem)
                        loadEnd = viewport.end + loadZoneSize;
                    else
                        return this.lastQuery;
                }
                break;
        }

        // adjust to existing data range
        if (loadStart < alreadyLoaded.start && rs.hasVeryFirstItem)
            loadStart = alreadyLoaded.start;
        if (loadEnd > alreadyLoaded.end && rs.hasVeryLastItem)
            loadEnd = alreadyLoaded.end;
        const loadZone = new NumberRange(loadStart, loadEnd);

        if (this.items.size == 0) // No entries -> nothing to "align" the query to
            return this.lastQuery;

        if (alreadyLoaded.contains(loadZone)) {
            // debug helper
            // console.warn('already!', viewport, alreadyLoaded, loadZone);
            return this.lastQuery;
        }

        let startIndex = -1;
        let endIndex = -1;
        const items = [...this.orderedItems];
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (!item.isChatEntry)
                continue;

            if (item.range.size == 0)
                continue; // skip items with zero height

            if (item.isMeasured && item.range.intersectWith(loadZone).size > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            } else if (startIndex >= 0)
                break;
        }
        const keyItemDistance = endIndex - startIndex;
        const firstItem = items[startIndex]
            ?? items[0].range.start > loadZone.end
                ? items[0]
                : items[items.length - 1];
        const lastItem = items[endIndex] ?? firstItem;
        // Calculate move range and keep it within at least one boundary of the key items
        const moveRangeStart = clamp(Math.ceil((loadZone.start - firstItem.range.start) / itemSize / responseFulfillmentRatio), -Infinity, keyItemDistance);
        const moveRangeEnd = clamp(Math.ceil((loadZone.end - lastItem.range.end) / itemSize / responseFulfillmentRatio), -keyItemDistance, Infinity);
        const moveRange = new NumberRange(moveRangeStart, moveRangeEnd);
        const startGap = Math.max(0, firstItem.range.start - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastItem.range.end);
        // skip queries that load few more items - we prefer to load more - if not close of the skeletons
        const smallGap = viewportSize * 0.5;
        const isFirstItemInViewport = !rs.hasVeryFirstItem && firstItem.range.end >= viewport.start;
        const isLastItemInViewport = !rs.hasVeryLastItem && lastItem.range.start <= viewport.end;
        if (startGap < smallGap && endGap < smallGap && firstItem.range.start && !isFirstItemInViewport && !isLastItemInViewport)
            return this.lastQuery;

        const keyRange = new Range(firstItem.key, lastItem.key);
        const query = new VirtualListDataQuery(keyRange, loadZone, moveRange);
        query.expectedCount = Math.ceil(loadZone.size / this.statistics.itemSize);
        return query;
    }
}

// Helper functions
function getItemKey(itemRef?: HTMLElement): string | null {
    // return itemRef?.dataset['key'];
    return itemRef?.id;
}

function getItemCountAs(itemRef?: HTMLElement): number {
    if (itemRef == null)
        return null;

    const sCountAs = itemRef.dataset['countAs'];
    return sCountAs == null ? 1 : parseInt(sCountAs);
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
