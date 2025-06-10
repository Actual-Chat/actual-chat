import { debounce, PromiseSource, PromiseSourceWithTimeout, throttle } from 'promises';
import { NumberRange, Range } from './ts/range';
import { VirtualListEdge } from './ts/virtual-list-edge';
import { VirtualListStickyEdgeState } from './ts/virtual-list-sticky-edge-state';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListItem } from './ts/virtual-list-item';
import { VirtualListStatistics } from './ts/virtual-list-statistics';
import { Pivot } from './ts/pivot';

import { Log } from 'logging';
import { fastRaf, fastReadRaf } from 'fast-raf';
import { DeviceInfo } from 'device-info';
import { clamp } from 'math';
import { BrowserInfo } from '../../Services/BrowserInfo/browser-info';

const { warnLog, debugLog } = Log.get('VirtualList');

const UpdateViewportInterval: number = 64;
const UpdateItemVisibilityInterval: number = 250;
const VisibilityEpsilon: number = 4;
const EdgeEpsilon: number = 4;
const ScrollDebounce: number = 200;
const SkeletonDetectionBoundary: number = 200;
const MinViewPortSize: number = 400;
const RequestDataTimeout: number = 800;

type ScrollToEdgeReason = 'no-pivot' | 'last-item' | 'sticky-edge' | 'non-item-resize' | 'item-resize' | 'unknown';
interface ScrollMetadata {
    shouldUseSmoothScroll: boolean;
    scrollType: ScrollToEdgeReason;
    scroll?: () => void;
}

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
    private readonly rowGap: number = 2;

    private isDisposed = false;
    private cachedAllItemRefs: Array<HTMLElement> | null = null;
    private stickyEdge: Required<VirtualListStickyEdgeState> | null = null;
    private whenRequestDataCompleted: PromiseSource<void> | null = null;
    private pivots: Pivot[] = [];
    private minStart: number | null = null;
    private isStartKnown: boolean = false;
    private maxEnd: number | null = null;
    private isEndKnown: boolean = false;
    private windowScrollTop: number = 0;

    private renderStartedAt: number | null = null;
    private renderCompletedAt: number = 0;
    private scrollPositionRestoredAt: number = 0;
    private isNearSkeleton: boolean = false;
    private isEndAnchorVisible: boolean = false;
    private isScrolling: boolean = false;
    private scrollTime: number | null = null;
    private scrollDirection: 'up' | 'down' | 'none' = 'none';
    private turnOffScrollingCallback?: () => void = null;

    private query: VirtualListDataQuery = VirtualListDataQuery.None;
    private lastQuery: VirtualListDataQuery = VirtualListDataQuery.None;
    private lastQueryTime: number | null = null;

    private renderState: VirtualListRenderState;
    private orderedItems: VirtualListItem[] = [];
    private itemRange: NumberRange | null = null;
    private viewport: NumberRange | null = null;
    private lastViewport: NumberRange | null = null;
    private endAnchorSize = 4;
    private shouldUpdateCornerstoneItem: boolean = true;
    private isUpdatingPivots: boolean = false;

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
        this.expandMultiplier = expandMultiplier;

        this.items = new Map<string, VirtualListItem>();
        this.sizeCache = new Map<string, number>();

        this.isDisposed = false;
        this.abortController = new AbortController();
        this.wrapperRef = this.ref.querySelector(':scope > .c-wrapper');
        this.containerRef = this.wrapperRef.querySelector(':scope > .c-virtual-container');
        this.spacerRef = this.containerRef.querySelector(':scope > .c-spacer-start');
        this.endSpacerRef = this.containerRef.querySelector(':scope > .c-spacer-end');
        this.renderStateRef = this.ref.querySelector(':scope > .data.render-state');
        this.renderIndexRef = this.ref.querySelector(':scope > .data.render-index');
        this.endAnchorRef = this.wrapperRef.querySelector(':scope > .c-end-anchor');
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;
        this.endAnchorSize = this.endAnchorRef.getBoundingClientRect().height;

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

        this.renderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,
            keyRange: new Range<string>('', ''),
            beforeCount: null,
            afterCount: null,
            estimatedCount: null,
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
                const time = Date.now();
                debugLog?.log(`renderStartedAt: `, time, value);
                this.renderStartedAt = time;
                origSetAttribute.call(this.renderIndexRef, qualifiedName, value);
                fastRaf(() => {
                    void this.endRender();
                });
            } catch (e) {
                warnLog?.log('renderIndex.setAttribute: failed', e);
            }
        };
        if (this.parseRenderState() === null)
            this.renderStartedAt = Date.now();

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
        this.onItemSetChange([mutationRecord], this.itemSetChangeObserver);};

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
        this.ref.removeEventListener('scroll', this.onScroll);
        this.ref.removeEventListener('scrollend', this.onScrollEnd);
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
        this.sizeCache.clear();
        this.orderedItems = [];
        this.pivots = [];
        this.minStart = null;
        this.maxEnd = null;
        this.isStartKnown = false;
        this.isEndKnown = false;
        this.renderState = {
            renderIndex: -1,
            query: VirtualListDataQuery.None,
            keyRange: new Range<string>('', ''),
            beforeCount: null,
            afterCount: null,
            estimatedCount: null,
            count: 0,
            hasVeryFirstItem: false,
            hasVeryLastItem: false,

            scrollToKey: null,
        };
    }

    /** Called by blazor */
    public renderSkipped(): void {
        debugLog?.log(`renderSkipped()`);
        this.renderStartedAt = null;
        this.renderCompletedAt = Date.now();
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
    }

    private get isRendering(): boolean {
        return !!this.renderStartedAt;
    }

    private get isInitialRender(): boolean {
        const now = Date.now();
        // debugLog?.log('scrollToEdge: schedule', edge, useSmoothScroll, reason);
         // first 1.5 seconds after creating the virtual list
        return now - this.createdAt < 1500;
    }

    private get hasUnmeasuredItems(): boolean {
        return this.unmeasuredItems.size > 0 || !this.orderedItems;
    }

    private get knownRange(): NumberRange | null {
        return this.minStart == null || this.maxEnd == null
            ? null
            : new NumberRange(this.minStart, this.maxEnd);
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
        this.itemRange = null;

        // copy existing items - because we can remove them and add again at another tiles
        for (const mutation of mutations) {
            if (mutation.type !== 'childList')
                continue;

            for (const node of mutation.removedNodes) {
                const nodeElement = node as HTMLElement;
                const isGroup = nodeElement.classList && nodeElement.classList.contains('group');
                if (!node['dataset'] && !isGroup)
                    continue;

                const itemRefs = this.getChildItemRefs(nodeElement);
                for (const itemRef of itemRefs) {
                    const key = getItemKey(itemRef);
                    this.items.delete(key);
                    this.unmeasuredItems.delete(key);
                    this.visibleItems.delete(key);
                    this.sizeObserver.unobserve(itemRef);
                    this.visibilityObserver.unobserve(itemRef);
                    itemRef.removeEventListener('touchend', this.onInteractiveEvent);
                    itemRef.removeEventListener('click', this.onInteractiveEvent);
                }
            }
            for (const node of mutation.addedNodes) {
                const nodeElement = node as HTMLElement;
                const isGroup = nodeElement.classList && nodeElement.classList.contains('group');
                if (!node['dataset'] && !isGroup)
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

                    const oldItem = this.items.get(key);
                    const newItem = this.createListItem(key, itemRef);
                    if (oldItem) {
                        oldItem.range = null; // reset range
                        oldItem.size = newItem.size;
                        oldItem.shouldSkipKey = newItem.shouldSkipKey;
                        if (oldItem.size > 0)
                            this.unmeasuredItems.delete(key);
                    } else
                        this.items.set(key, newItem);
                }
            }
        }


        this.updateOrderedItems();
        if (this.renderState.renderIndex <= 0)
            void this.endRender();
    };

    private onResize = (entries: ResizeObserverEntry[], _observer: ResizeObserver): void => {
        //console.warn(`onResize: entries =`, [...entries]);
        let itemsWereMeasured = false;
        let notAnItem = false;
        let existingResizedCount = 0;
        const itemRefsWithWrongSize = new Array<HTMLElement>();
        for (const entry of entries) {
            const rect = entry.contentRect;
            const key = getItemKey(entry.target as HTMLElement);
            const rowGap = this.rowGap;
            const size = Math.ceil(rect.height + rowGap);
            if (!key) {
                notAnItem = true;
                if (entry.target === this.endAnchorRef)
                    this.endAnchorSize = size;
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
                    if (oldSize > 0 && size > 0 && size != oldSize) {
                        existingResizedCount++;
                        itemsWereMeasured = true;
                    }
                    item.size = size;
                    item.range = null;
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
                this.unmeasuredItems.add(key);
            });
            fastRaf(() => this.measureItems());
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
        }

        // recalculate item range as some elements were updated
        if (itemsWereMeasured || existingResizedCount > 0) {
            this.itemRange = null;
            const renderState = { ...this.renderState, scrollToKey: undefined };
            const scrollMetadata = this.getScrollMetadata(renderState);
            void this.restoreScrollPosition(renderState, scrollMetadata);
        }
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
            this.isNearSkeleton = isNearSkeleton;
            // reset turn off attempt
            this.turnOffIsNearSkeletonDebounced.reset();
            // this.updateViewportThrottled();
        } else
            this.turnOffIsNearSkeletonDebounced();
    };

    private turnOffIsNearSkeletonDebounced = debounce(() => this.turnOffIsNearSkeleton(), ScrollDebounce);

    private turnOffIsNearSkeleton(): void {
        this.isNearSkeleton = false;
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

        const startedAt = this.renderStartedAt;
        const now = Date.now();
        debugLog?.log(`endRender, renderIndex = #${rs.renderIndex}, duration = ${now - startedAt}ms, rs =`, rs);

        try {
            // Update statistics
            if (!rs.query.isNone && rs.query.expectedCount)
                this.statistics.addResponse(rs.count, rs.query.expectedCount);

            const scrollMetadata = this.getScrollMetadata(rs);

            await this.restoreScrollPosition(rs, scrollMetadata);
        } finally {
            this.renderStartedAt = null;
            this.renderCompletedAt = Date.now();
            this.whenRequestDataCompleted?.resolve(undefined);
            this.whenRequestDataCompleted = null;

            this.lastViewport = this.viewport;
        }
    }

    private getScrollMetadata(rs: VirtualListRenderState): ScrollMetadata {
        const scrollToItemRef = this.getItemRef(rs.scrollToKey);
        let shouldUseSmoothScroll = false;
        let scrollType: ScrollToEdgeReason = 'unknown';
        let scrollFunc: (() => void) | null = null;

        if (scrollToItemRef != null) {
            // Server-side scroll request
            if (!this.isKeyVisible(rs.scrollToKey)) {
                if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                    scrollType = 'last-item';
                    shouldUseSmoothScroll = this.stickyEdge?.edge == VirtualListEdge.End;
                    scrollFunc = () => {
                        this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, scrollType);
                        this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                    };
                } else {
                    scrollFunc = () => this.scrollTo(scrollToItemRef, false);
                }
            } else if (rs.scrollToKey === this.getLastItemKey() && rs.hasVeryLastItem) {
                shouldUseSmoothScroll = true;
                scrollType = 'last-item';
                scrollFunc = () => {
                    this.scrollToEdge(VirtualListEdge.End, shouldUseSmoothScroll, scrollType);
                    this.setStickyEdge({ itemKey: rs.scrollToKey, edge: VirtualListEdge.End });
                };
            }
        } else if (this.query.isNone && this.stickyEdge != null) {
            // Sticky edge scroll when we are not requesting data with query - render of new items only
            const itemKey = this.stickyEdge.edge === VirtualListEdge.Start && rs.hasVeryFirstItem
                ? this.getFirstItemKey()
                : this.stickyEdge.edge === VirtualListEdge.End && rs.hasVeryLastItem
                    ? this.getLastItemKey()
                    : null;
            if (itemKey) {
                shouldUseSmoothScroll = itemKey !== this.stickyEdge.itemKey;
                scrollType = 'sticky-edge';
                scrollFunc = () => {
                    this.setStickyEdge({ itemKey, edge: this.stickyEdge.edge });
                    this.scrollToEdge(this.stickyEdge.edge, shouldUseSmoothScroll, scrollType);
                };
            } else {
                if (this.stickyEdge.edge === VirtualListEdge.End) {
                    const itemRef = this.getItemRef(this.stickyEdge.itemKey);
                    scrollFunc = () => this.scrollTo(itemRef, false);
                }
                this.setStickyEdge(null);
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
        'default',
        'updateViewport');

    private async updateViewport(isThrottled = false): Promise<void> {
        const rs = this.renderState;
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

        if (this.isDisposed || this.isRendering)
            return;

        this.viewport = this.calculateViewport();
        await this.requestData();
    }

    private calculateViewport(): NumberRange {
        const viewportHeight = this.ref.clientHeight;
        const scrollTop = this.ref.scrollTop;
        const viewport = this.defaultEdge === VirtualListEdge.End
            ? new NumberRange(scrollTop - viewportHeight, scrollTop)
            : new NumberRange(scrollTop, scrollTop + viewportHeight);

        const oldViewport = this.viewport ?? this.lastViewport;
        if (oldViewport && viewport) {
            if (viewport.start < oldViewport.start)
                this.scrollDirection = 'up';
            else
                this.scrollDirection = 'down';
        }
        return viewport;
    }

    private readonly updateVisibleKeysThrottled = throttle(
        () => this.updateVisibleKeys(),
        UpdateItemVisibilityInterval,
        'delayHead',
        'updateVisibleKeys');
    private async updateVisibleKeys(): Promise<void> {
        if (this.isDisposed || !this.renderState.keyRange.start)
            return;

        const visibleItems = [...this.visibleItems].sort(this.keySortCollator.compare);
        const isEndAnchorVisible = this.stickyEdge?.edge === VirtualListEdge.End;
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
        this.orderedItems = orderedItems;
    }

    private createListItem(itemKey: string, itemRef: HTMLElement): VirtualListItem {
        const newItem = new VirtualListItem(itemKey);
        const size = this.sizeCache.get(itemKey);
        if (size > 0)
            newItem.size = size;
        else
            this.unmeasuredItems.add(itemKey);
        this.sizeObserver.observe(itemRef, { box: 'border-box' });
        this.visibilityObserver.observe(itemRef);
        itemRef.addEventListener('touchend', this.onInteractiveEvent, { passive: true });
        itemRef.addEventListener('click', this.onInteractiveEvent, { passive: true });
        newItem.shouldSkipKey = itemRef.dataset['skip'] === 'true';
        return newItem;
    }

    // Event handlers

    private onScroll = (ev: Event): void => {
        this.isScrolling = true;
        this.turnOffIsScrollingDebounced();

        if (this.isRendering)
            return;

        if (!ev.isTrusted)
            return; // Ignore non-user initiated scrolls

        // Reset pivots on scroll
        this.pivots = [];
        this.updateViewportThrottled();
    };

    private onScrollEnd = (ev: Event): void => {
        this.turnOffIsScrolling();
    }

    private scheduleUpdateCurrentPivots(interactiveKey?: string): void {
        if (this.isDisposed)
            return;

        fastRaf(() => this.updateCurrentPivots(interactiveKey));
    }

    private updateCurrentPivots(interactiveKey?: string): void {
        if (this.isRendering)
            return;
        if (this.isUpdatingPivots)
            return;

        try {
            this.isUpdatingPivots = true;

            const time = Date.now();
            const pivots = new Array<Pivot>();
            const pivotRefs = new Array<HTMLElement>();
            // add query edges and second\last items as pivots

            // do not use first item as pivot - it might be changed during rendering of items above - e.g. author circle might disappear

            let medianVisibleKey = null;
            if (this.visibleItems.size) {
                const visibleItems = [...this.visibleItems.values()];
                medianVisibleKey = visibleItems[Math.floor(visibleItems.length / 2)];
            }

            const viewRect = this.ref.getBoundingClientRect();
            const itemKeys: string[] = [interactiveKey, medianVisibleKey, this.query.keyRange?.end, this.query.keyRange?.start];
            for (let itemKey of itemKeys) {
                if (!itemKey)
                    continue;

                const item = this.items.get(itemKey);
                const pivotRef = this.getItemRef(itemKey);
                if (!pivotRef)
                    continue;

                pivotRefs.push(pivotRef);
                // measure scroll position
                const itemRect = pivotRef.getBoundingClientRect();
                const isVisible = this.isRectIntersects(itemRect, viewRect);
                const isInteractive = itemKey === interactiveKey;
                const pivot: Pivot = {
                    itemKey,
                    offset: Math.round(itemRect.top),
                    range: item?.range,
                    time,
                    isVisible,
                    isInteractive
                };
                pivots.push(pivot);
            }
            this.pivots = pivots;
        }
        finally {
            this.isUpdatingPivots = false;
            // if (interactiveKey)
            //     void this.restoreScrollPosition(this.renderState);
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

        const turnOffScrollingCallback = this.turnOffScrollingCallback;
        if (turnOffScrollingCallback) {
            this.turnOffScrollingCallback = null;
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

    private getItemRef(key: string): HTMLElement | null {
        if (key == null)
            return null;

        return this.containerRef.querySelector(`:scope .item[data-key="${key}"]`);
    }

    private getFirstItemRef(): HTMLElement | null {
        let ref = this.containerRef.firstElementChild.nextElementSibling; // skip spacer
        if (ref == null)
            return null;

        if (ref.classList.contains('item'))
            return ref as HTMLElement;

        if (ref.classList.contains('group')) {
            while (ref) {
                ref = ref.lastElementChild;
                if (ref.classList.contains('item')) {
                    // we have found list item in the group, let's find the first one
                    ref = ref.parentElement.firstElementChild;
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
        let ref = this.containerRef.lastElementChild.previousElementSibling; // skip end spacer
        if (ref == null)
            return null;

        if (ref.classList.contains('item'))
            return ref as HTMLElement;

        if (ref.classList.contains('group')) {
            while (ref) {
                ref = ref.lastElementChild;
                if (ref.classList.contains('item'))
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

        if (this.renderState.renderIndex <= 1 || isInitialRender)
            useSmoothScroll = false; // fix for scroll to the end on chat switch
        this.scrollTime = Date.now();

        let scrollHeight = 0;
        fastRaf({
            read: () => {
                const isFarFromEdge = edge == VirtualListEdge.End
                    ? -this.ref.scrollTop > this.ref.offsetHeight
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

    private async restoreScrollPosition(rs: VirtualListRenderState, scrollMetadata: ScrollMetadata | null = null, useRaf = false): Promise<void> {
        const { hasUnmeasuredItems, defaultSpacerSize, endAnchorSize } = this;
        const result = new PromiseSource();
        // debugLog?.log(`restoreScrollPosition: start`);

        const pivots = [...this.pivots];
        const interactivePivot = pivots.find(p => p.isInteractive);

        let scrollTop = 0;
        let scrollTopOffset = 0;
        let offset = 0;
        let totalSize = 0;
        let beforeSize = 0;
        let afterSize = 0;
        let spacerSize = 0;
        let endSpacerSize = 0;
        let totalSizeDiff = 0;
        let isInteractivePositioning = false;

        // Cancel any pending viewport calculations
        this.updateViewportThrottled.reset();

        const options = {
            key: `restoreScrollPosition_${this.identity}`,
            read: () => {
                if (hasUnmeasuredItems)
                    this.measureItems();
                if (!this.itemRange)
                    this.ensureItemRangeCalculated();

                const orderedItems = [... this.orderedItems];
                const { start, end, size: itemRangeSize } = this.itemRange ?? new NumberRange(0,0);
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
                        : null;

                    let fullRange: NumberRange;
                    if (this.isStartKnown && this.isEndKnown)
                        fullRange = new NumberRange(knownRange.start, knownRange.end + endAnchorSize);
                    else if (this.isStartKnown) {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size + defaultSpacerSize);
                        fullRange = new NumberRange(knownRange.start, fullRangeSize);
                    }
                    else if (this.isEndKnown) {
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
                            offset = this.itemRange.end;
                        }
                    }
                    else if (rs.hasVeryLastItem && offset < -endAnchorSize) {
                        // reset if we are at the end anchor and offset is less than end anchor size
                        this.resetItemRange();
                        scrollTopOffset = 0;
                        offset = this.itemRange.end;
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
                            offset = this.itemRange.start;
                        }
                    }
                    else if (rs.hasVeryFirstItem && offset > 0) {
                        // reset if we are at the start and the offset is greater than 0
                        this.resetItemRange();
                        scrollTopOffset = 0;
                        offset = this.itemRange.start;
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

                if (totalSizeDiff != 0 && this.isScrolling && rs.renderIndex > 0) {
                    // delay wrapper size increase when scrolling in Chromium to prevent issues with scroll position jumps
                    const setWrapperHeight = () => fastRaf({
                        write: () => {
                            if (this.isScrolling)
                                this.turnOffScrollingCallback = setWrapperHeight;
                            else {
                                this.wrapperRef.style.height = `${totalSize}px`;
                                // console.warn(
                                //     'restoreScrollPosition: wrapper size increased with DELAY!',
                                //     totalSize);
                            }
                        }});
                    this.turnOffScrollingCallback = setWrapperHeight;

                }
                else if (totalSizeDiff != 0) {
                    this.wrapperRef.style.height = totalSize + 'px';
                }

                if (this.defaultEdge === VirtualListEdge.End) {
                    this.containerRef.style.bottom = `${-offset}px`;
                }
                else {
                    this.containerRef.style.top = `${offset}px`;
                }
                if (scrollTopOffset) {
                    this.ref.scrollTop = scrollTop + scrollTopOffset;
                }
                // debugLog?.log(`restoreScrollPosition: scroll set`, offset, totalSize, scrollTop, spacerSize, endSpacerSize);

                result.resolve(undefined);
            }
        };
        if (useRaf) {
            fastRaf(options);
            await result;
        }
        else {
            // Handle restore position synchronously after render
            options.read();
            options.write();
            if (!isInteractivePositioning)
                scrollMetadata?.scroll?.();
        }

        this.scrollPositionRestoredAt = Date.now();

        this.updateViewportThrottled();
        // await delayAsync(50);
        // debugLog?.log(`restoreScrollPosition: end`, rafResult);
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

            const itemSizeIsValid = item.size > 0;
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
                item.range = null;
                itemsWereMeasured = true;
                this.sizeCache.set(key, size);
                removeUnmeasuredItem(key);
            }
        }

        // recalculate item range as some elements were updated
        if (itemsWereMeasured) {
            this.itemRange = null;
            this.ensureItemRangeCalculated();
        }
    }

    private ensureItemRangeCalculated(): boolean {
        // this function is expected to be called with RAF
        if (this.hasUnmeasuredItems) {
            this.measureItems();
        }

        if (this.itemRange)
            return false;

        const { renderState: rs, orderedItems, visibleItems, pivots, defaultEdge, statistics } = this;

        // nothing to do when there are no items rendered
        if (orderedItems.length == 0)
            return false;

        // TODO: validate idea of recalculating ranges
        // if (this.shouldUpdateCornerstoneItem && (rs.hasVeryFirstItem || rs.hasVeryLastItem)) {
        //     // We have to recalculate the cornerstone item
        //     this.shouldUpdateCornerstoneItem = false;
        //     for (const item of orderedItems)
        //         item.range = null;
        // }

        let cornerstoneItemIndex = -1;
        let cornerstoneItem: VirtualListItem = null;
        const pivotRanges = pivots.map(p =>
            defaultEdge === VirtualListEdge.End
                ? new NumberRange(p.range.start - statistics.itemSize, p.range.end)
                : new NumberRange(p.range.start, p.range.end + statistics.itemSize)); // expand pivot ranges to cover the nearest items in stable direction
        const visibleItemRanges = [...visibleItems.keys()].map(k => this.items.get(k)?.range);
        const cornerstoneRanges  = [...pivotRanges, ...visibleItemRanges].filter(r => r);
        const orderedItemsWithRange = orderedItems.filter(item => item.range);
        if (cornerstoneRanges.length > 0) {
            for (const cornerstoneRange of cornerstoneRanges) {
                const index = binarySearch(orderedItemsWithRange, it => !it.range.intersectWith(cornerstoneRange).isEmpty || it.range.start >= cornerstoneRange.start);
                if (index !== -1) {
                    cornerstoneItemIndex = index;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                    break;
                }
            }
        }
        if (!cornerstoneItem || !cornerstoneItem.range) {
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
            (cornerstoneItem?.range?.end ?? 0) < -this.endAnchorSize;
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

        // Adjust item ranges according to default edge invariant
        if (!rs.query.isNone) {
            if (this.defaultEdge === VirtualListEdge.End) {
                const end = orderedItems[orderedItems.length - 1].range.end - this.rowGap;
                if (end > 0) {
                    cornerstoneItemIndex = orderedItems.length - 1;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                    cornerstoneItem.range = new NumberRange(0 - cornerstoneItem.size, 0);
                    let prevItem = cornerstoneItem;
                    for (let i = cornerstoneItemIndex - 1; i >= 0; i--) {
                        const item = orderedItems[i];
                        item.range = new NumberRange(prevItem.range.start - item.size, prevItem.range.start);
                        prevItem = item;
                    }
                }
            } else {
                const start = orderedItems[0].range.start + this.rowGap;
                if (start < 0) {
                    cornerstoneItemIndex = 0;
                    cornerstoneItem = orderedItems[cornerstoneItemIndex];
                    cornerstoneItem.range = new NumberRange(0, cornerstoneItem.size);
                    let prevItem = cornerstoneItem;
                    for (let i = cornerstoneItemIndex + 1; i < orderedItems.length; i++) {
                        const item = orderedItems[i];
                        item.range = new NumberRange(prevItem.range.end, prevItem.range.end + item.size);
                        prevItem = item;
                    }
                }
            }
        }

        this.itemRange = new NumberRange(
            orderedItems[0].range.start,
            orderedItems[orderedItems.length - 1].range.end - this.rowGap);

        this.minStart = Math.min(this.minStart ?? Number.MAX_SAFE_INTEGER, this.itemRange.start);
        if (this.renderState.hasVeryFirstItem)
            this.isStartKnown = true;
        this.maxEnd = Math.max(this.maxEnd ?? Number.MIN_SAFE_INTEGER, this.itemRange.end);
        if (this.renderState.hasVeryLastItem)
            this.isEndKnown = true;

        return true;
    }

    private recalculateItemRangesFromCornerstone(orderedItems: VirtualListItem[], cornerstoneItemIndex: number): void {
        const cornerstoneItem = orderedItems[cornerstoneItemIndex];
        let prevItem = cornerstoneItem;
        for (let i = cornerstoneItemIndex + 1; i < orderedItems.length; i++) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.end, prevItem.range.end + item.size);
            prevItem = item;
        }
        prevItem = cornerstoneItem;
        for (let i = cornerstoneItemIndex - 1; i >= 0; i--) {
            const item = orderedItems[i];
            item.range = new NumberRange(prevItem.range.start - item.size, prevItem.range.start);
            prevItem = item;
        }
    }

    private resetItemRange(canUseViewport: boolean = false): number | null {
        // This function is expected to be called with RAF
        const { orderedItems, defaultSpacerSize, endAnchorSize, renderState: rs } = this;
        const fullRangeSize = this.knownRange?.size;

        const viewport = this.viewport = this.calculateViewport();
        if (orderedItems.length === 0)
            return null;

        let rangeDelta: number | null = null;
        const originalRanges = orderedItems.map(item => ({ ...item.range }) as Range<number>);

        function findCenterItemIndex() {
            // Find item index closest to the viewport center
            const totalSize = orderedItems.reduce((sum, item) => sum + item.size, 0);
            let runningSize = 0;
            let cornerstoneItemIndex = 0;
            for (let i = 0; i < orderedItems.length; i++) {
                runningSize += orderedItems[i].size;
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
                    0 - Math.floor(rs.afterCount * this.statistics.itemSize) - cornerstoneItem.size,
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
                    Math.floor(viewportCenter - cornerstoneItem.size / 2),
                    Math.ceil(viewportCenter + cornerstoneItem.size / 2)
                );
            }
            else if (!rs.hasVeryLastItem) {
                // There is no query range and no very last item, so we have to calculate range manually with end spacer
                cornerstoneItem.range = new NumberRange(
                    0 - defaultSpacerSize - endAnchorSize - cornerstoneItem.size,
                    0 - defaultSpacerSize - endAnchorSize);
            }
            else
                cornerstoneItem.range = new NumberRange(
                    0 - endAnchorSize - cornerstoneItem.size,
                    0 - endAnchorSize);

            this.shouldUpdateCornerstoneItem = !rs.hasVeryLastItem;
            this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);

            rangeDelta = Math.max(...originalRanges.map((r, i) => orderedItems[i].range.end - r.end));
            this.itemRange = new NumberRange(
                orderedItems[0].range.start,
                orderedItems[orderedItems.length - 1].range.end);
            if (fullRangeSize) {
                this.minStart = 0 - fullRangeSize - endAnchorSize;
                this.maxEnd = 0  - endAnchorSize;
                // Do not reset isStartKnown \ isEndKnown as knownRange size has not changed
            }
            else {
                this.minStart = orderedItems[0].range.start;
                this.maxEnd = orderedItems[orderedItems.length - 1].range.end;
                this.isEndKnown = rs.hasVeryLastItem;
                this.isStartKnown = rs.hasVeryFirstItem;
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
                    Math.floor(rs.beforeCount * this.statistics.itemSize) + cornerstoneItem.size);
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
                    Math.floor(viewportCenter - cornerstoneItem.size / 2),
                    Math.ceil(viewportCenter + cornerstoneItem.size / 2)
                );
            }
            else if (!rs.hasVeryFirstItem) {
                // There is no query range and no very first item, so we have to calculate range manually with spacer
                cornerstoneItem.range = new NumberRange(
                    defaultSpacerSize,
                    defaultSpacerSize + cornerstoneItem.size);
            }
            else
                cornerstoneItem.range = new NumberRange(
                    0,
                    cornerstoneItem.size);

            this.shouldUpdateCornerstoneItem = !rs.hasVeryFirstItem;
            this.recalculateItemRangesFromCornerstone(orderedItems, cornerstoneItemIndex);
            rangeDelta = Math.max(...originalRanges.map((r, i) => orderedItems[i].range.start - r.start));
            this.itemRange = new NumberRange(
                orderedItems[0].range.start,
                orderedItems[orderedItems.length - 1].range.end);
            if (fullRangeSize) {
                this.minStart = 0;
                this.maxEnd = fullRangeSize + endAnchorSize;
                // Do not reset isStartKnown \ isEndKnown as knownRange size has not changed
            }
            else {
                this.minStart = orderedItems[0].range.start;
                this.maxEnd = orderedItems[orderedItems.length - 1].range.end;
                this.isEndKnown = rs.hasVeryLastItem;
                this.isStartKnown = rs.hasVeryFirstItem;
            }
            return rangeDelta;
        }
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this.viewport)
            return;

        const query = this.getDataQuery();
        // if (this.renderState.renderIndex > 0)
        //     return;// this.lastQuery; // Debug helper
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

        debugLog?.log(`requestData: query:`, query, query.virtualRange, this.itemRange);
        this.lastQueryTime = Date.now();
        // debug helper
        // await delayAsync(1500);
        await this.blazorRef.invokeMethodAsync('RequestData', this.query);
        this.lastQuery = this.query;
    }

    private mustRequestData(query: VirtualListDataQuery): boolean {
        const queryRange = query.virtualRange;
        const { itemRange, viewport, renderState: rs } = this;
        if (!itemRange || !queryRange)
            return false;

        if (!viewport)
            return false;

        if (itemRange.isEmpty)
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
        // if (rs.renderIndex > 0)
        //     return this.lastQuery; // Debug helper

        const itemSize = this.statistics.itemSize;
        const viewport = this.viewport;
        this.ensureItemRangeCalculated();
        const orderedItems = [...this.orderedItems.filter(i => !i.shouldSkipKey)];
        if (orderedItems.length == 0) // No entries -> nothing to "align" the query to
            return this.lastQuery;

        if (orderedItems.some(item => item.range == null)) {
            this.itemRange = null;
            this.ensureItemRangeCalculated();
        }

        const alreadyLoaded = this.itemRange;
        if (!viewport || !alreadyLoaded)
            return this.lastQuery;

        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return this.lastQuery; // We have already loaded all data

        if (this.isRendering)
            return this.lastQuery; // Do not request data during rendering as it might be inconsistent

        const now = Date.now();
        if (now - this.renderCompletedAt < 500 && this.lastQuery.isNone)
            return this.lastQuery; // Do not request data during the first second after render caused by updated data (not scroll)

        const viewportSize = viewport.size;
        const alreadyLoadedFromStart = viewport.start - alreadyLoaded.start;
        const alreadyLoadedTillEnd = alreadyLoaded.end - viewport.end;
        const loadZoneTrigger = viewportSize * Math.max(0.5, this.expandMultiplier * 0.5);
        if (alreadyLoadedFromStart > loadZoneTrigger && alreadyLoadedTillEnd > loadZoneTrigger)
            return this.lastQuery; // No need to load more data

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
            return this.lastQuery;
        }

        const lastKey = orderedItems[orderedItems.length - 1].key;
        const firstItemIndex = binarySearch(orderedItems, item => item.range.end >= loadZone.start);
        const lastItemIndex = binarySearch(orderedItems, item => item.range.start > loadZone.end || (item.key === lastKey && !item.range.intersectWith(loadZone).isEmpty));
        let firstItem = orderedItems[firstItemIndex];
        let lastItem = orderedItems[lastItemIndex];
        if (!firstItem) {
            if (orderedItems[0].range.start >= loadZone.end)
                firstItem = orderedItems[0];
            else if (orderedItems[orderedItems.length - 1].range.end <= loadZone.start)
                firstItem = orderedItems[orderedItems.length - 1];
            else
                firstItem = orderedItems[0];
        }
        if (!lastItem) {
            if (orderedItems[orderedItems.length - 1].range.end <= loadZone.start)
                lastItem = orderedItems[orderedItems.length - 1];
            else if (orderedItems[0].range.start >= loadZone.end)
                lastItem = orderedItems[0];
            else
                lastItem = orderedItems[orderedItems.length - 1];
        }
        const keyRange = new Range(firstItem.key, lastItem.key);
        const moveRangeStart = Math.floor((loadZone.start - firstItem.range.start) / itemSize / 5) * 5; // round to 5 to prevent too many requests
        const moveRangeEnd = Math.ceil((loadZone.end - lastItem.range.end) / itemSize / 5) * 5; // round to 5 to prevent too many requests
        const moveRange = new NumberRange(moveRangeStart, moveRangeEnd);
        const startGap = Math.max(0, firstItem.range.start - loadZone.start);
        const endGap = Math.max(0, loadZone.end - lastItem.range.end);
        // skip queries that load few more items - we prefer to load more - if not close of the skeletons
        const smallGap = viewportSize * 0.5;
        const isFirstItemInViewport = !rs.hasVeryFirstItem && firstItem.range.end >= viewport.start;
        const isLastItemInViewport = !rs.hasVeryLastItem && lastItem.range.start <= viewport.end;
        if (startGap < smallGap && endGap < smallGap && firstItem.range.start && !isFirstItemInViewport && !isLastItemInViewport)
            return this.lastQuery;

        const query = new VirtualListDataQuery(keyRange, loadZone, moveRange);
        query.expectedCount = Math.ceil(loadZone.size / this.statistics.itemSize);
        return query;
    }
}

// Helper functions
function getItemKey(itemRef?: HTMLElement): string | null {
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
