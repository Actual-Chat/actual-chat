import { debounce, PromiseSource, PromiseSourceWithTimeout, serialize, throttle } from 'promises';
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
    private readonly layoutFooter?: HTMLElement;
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
    private rowGap = 2;

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
    // private spacerSize: number | null = null;
    // private endSpacerSize: number | null = null;
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

        this.isDisposed = false;
        this.abortController = new AbortController();
        this.wrapperRef = this.ref.querySelector(':scope > .c-wrapper');
        this.spacerRef = this.wrapperRef.querySelector(':scope > .c-spacer-start');
        this.endSpacerRef = this.wrapperRef.querySelector(':scope > .c-spacer-end');
        this.containerRef = this.wrapperRef.querySelector(':scope > .c-virtual-container');
        this.renderStateRef = this.ref.querySelector(':scope > .data.render-state');
        this.renderIndexRef = this.ref.querySelector(':scope > .data.render-index');
        this.endAnchorRef = this.wrapperRef.querySelector(':scope > .c-end-anchor');
        this.layoutFooter = document.querySelector('.layout-body-wrapper > .c-container > .layout-footer');
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;
        this.endAnchorSize = this.endAnchorRef.getBoundingClientRect().height;

        // Set positioning according to the default edge
        if (this.defaultEdge === VirtualListEdge.Start) {
            this.containerRef.style.top = `${this.endAnchorSize}px`;
            this.ref.style.flexDirection = 'column';
            this.spacerRef.style.display = 'flex';
            this.endSpacerRef.style.display = 'none';
        }
        else {
            this.containerRef.style.bottom = `${this.endAnchorSize}px`;
            this.ref.style.flexDirection = 'column-reverse';
            this.spacerRef.style.display = 'none';
            this.endSpacerRef.style.display = 'flex';
        }
        this.wrapperRef.style.height = `100%`;

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

        this.sizeObserver.observe(this.layoutFooter);
        this.sizeObserver.observe(this.endAnchorRef, { box: 'border-box' });
        this.visibilityObserver.observe(this.endAnchorRef);
        this.skeletonObserver0.observe(this.spacerRef);
        this.skeletonObserver0.observe(this.endSpacerRef);
        this.skeletonObserver1.observe(this.spacerRef);
        this.skeletonObserver1.observe(this.endSpacerRef);

        this.items = new Map<string, VirtualListItem>();
        this.sizeCache = new Map<string, number>();
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
            } catch (e) {
                warnLog?.log('renderIndex.setAttribute: failed', e);
            }
        };
        if (this.parseRenderState() === null)
            this.renderStartedAt = Date.now();

        if (this.containerRef.classList.contains('hide')) {
            this.containerRef.classList.remove('hide');
            this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;
        }
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
        const oldItems = new Map<string, VirtualListItem>(this.items);
        for (const mutation of mutations) {
            if (mutation.type !== 'childList')
                continue;

            for (const node of mutation.removedNodes) {
                const nodeElement = node as HTMLElement;
                const isGroup = nodeElement.className === 'group';
                if (!node['dataset'] && !isGroup)
                    continue;

                const itemRefs = isGroup
                    ? [...nodeElement.children] as HTMLElement[]
                    : [nodeElement];
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
                const isGroup = nodeElement.className === 'group';
                if (!node['dataset'] && !isGroup)
                    continue;

                if (isGroup)
                    this.itemSetChangeObserver.observe(nodeElement, { childList: true });
                const itemRefs = isGroup
                    ? [...nodeElement.children] as HTMLElement[]
                    : [nodeElement];
                for (const itemRef of itemRefs) {
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
        }


        this.updateOrderedItems();
        void this.endRender();
    };

    private onResize = (entries: ResizeObserverEntry[], _observer: ResizeObserver): void => {
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
                if (item.size < 0 && size == 0) {
                    itemRefsWithWrongSize.push(entry.target as HTMLElement);
                } else {
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
        if (itemsWereMeasured)
            this.itemRange = null;

        const now = Date.now();
        if (existingResizedCount && (now - this.renderCompletedAt) > 500 && this.pivots.length > 0) {
            void this.restoreScrollPosition(this.renderState);
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
            this.viewport = null;
        }
    }

    private getScrollMetadata(rs: VirtualListRenderState): ScrollMetadata {
        if (!rs.query.isNone && rs.query.expectedCount)
            this.statistics.addResponse(rs.count, rs.query.expectedCount);

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
                shouldUseSmoothScroll = true;
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

        this.ensureItemRangeCalculated();

        const hasScheduled = await fastReadRaf(`updateViewport_${this.identity}`);
        if (!hasScheduled)
            return; // unable to schedule requestAnimationFrame, same key has already been scheduled

        if (this.isDisposed || this.isRendering)
            return;

        const viewportHeight = this.ref.clientHeight;
        const scrollTop = this.ref.scrollTop;
        const viewport = this.defaultEdge === VirtualListEdge.End
            ? new NumberRange(scrollTop - viewportHeight, scrollTop)
            : new NumberRange(scrollTop, scrollTop + viewportHeight);

        if (this.viewport && viewport) {
            if (viewport.start < this.viewport.start)
                this.scrollDirection = 'up';
            else
                this.scrollDirection = 'down';
        }

        this.viewport = viewport;
        await this.requestData();
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
        if (size)
            newItem.size = size;
        else
            this.unmeasuredItems.add(itemKey);
        this.sizeObserver.observe(itemRef, { box: 'border-box' });
        this.visibilityObserver.observe(itemRef);
        itemRef.addEventListener('touchend', this.onInteractiveEvent, { passive: true });
        itemRef.addEventListener('click', this.onInteractiveEvent, { passive: true });
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
                    range: item.range,
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
        const orderedItems = [... this.orderedItems];
        const interactivePivot = pivots.find(p => p.isInteractive);

        let scrollTop = 0;
        let scrollTopOffset = 0;
        let offset = 0;
        let totalSize = 0;
        let spacerSize = 0;
        let endSpacerSize = 0;
        let delayedSpacerSize = 0;
        let delayedEndSpacerSize = 0;
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

                const { start, end } = this.itemRange ?? new NumberRange(0,0);
                const containerSize = this.containerRef.offsetHeight;
                const oldTotalSize = this.wrapperRef.offsetHeight;

                scrollTop = this.ref.scrollTop;

                if (rs.beforeCount !== null && rs.afterCount !== null) {
                    spacerSize = Math.floor(rs.beforeCount * this.statistics.itemSize);
                    endSpacerSize =  Math.floor(rs.afterCount * this.statistics.itemSize);
                }
                else {
                    const knownRange = this.knownRange ?? new NumberRange(0,0);
                    const estimatedTotalSize = rs.estimatedCount
                        ? clamp(Math.floor(rs.estimatedCount * this.statistics.itemSize), knownRange.size, 5E6)
                        : null;

                    let fullRange: NumberRange;
                    if (this.isStartKnown && this.isEndKnown)
                        fullRange = knownRange;
                    else if (this.isStartKnown) {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size, oldTotalSize);
                        fullRange = new NumberRange(knownRange.start, fullRangeSize);
                    }
                    else if (this.isEndKnown) {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size, oldTotalSize);
                        fullRange = new NumberRange(knownRange.end - fullRangeSize, knownRange.end);
                    }
                    else {
                        const fullRangeSize = Math.max(estimatedTotalSize, knownRange.size, oldTotalSize);
                        fullRange = this.defaultEdge === VirtualListEdge.End
                            ? new NumberRange(knownRange.end - fullRangeSize, knownRange.end)
                            : new NumberRange(knownRange.start, knownRange.start + fullRangeSize)
                    }

                    spacerSize = clamp(start - fullRange.start, 0, fullRange.size - containerSize);
                    endSpacerSize = clamp(fullRange.end - end, 0, fullRange.size - containerSize);
                }
                if (spacerSize == 0 && !rs.hasVeryFirstItem)
                    spacerSize = defaultSpacerSize;
                if (endSpacerSize == 0 && !rs.hasVeryLastItem)
                    endSpacerSize = defaultSpacerSize;
                if (rs.hasVeryFirstItem)
                    spacerSize = 0;
                if (rs.hasVeryLastItem)
                    endSpacerSize = 0;

                totalSize = containerSize
                    + spacerSize
                    + endSpacerSize
                    + endAnchorSize;

                if (!rs.hasVeryFirstItem && !rs.hasVeryLastItem)
                    totalSize = Math.max(totalSize, oldTotalSize, end, -start);
                else if (rs.hasVeryFirstItem)
                    totalSize = Math.max(totalSize, -start);
                else if (rs.hasVeryLastItem)
                    totalSize = Math.max(totalSize, end);

                totalSizeDiff = totalSize - oldTotalSize;

                if (this.defaultEdge === VirtualListEdge.End) {
                    if (scrollMetadata?.shouldUseSmoothScroll && scrollMetadata?.scrollType === 'last-item') {
                        // Find previous item end to make smooth scroll possible with fallback to the latest one
                        const lastItem = orderedItems[orderedItems.length - 1];
                        offset = -endAnchorSize;
                        scrollTopOffset = -lastItem?.size ?? 0;
                    }
                    else
                        offset = end;

                    if (interactivePivot) {
                        let interactiveItemRef = this.getItemRef(interactivePivot.itemKey);
                        if (!interactiveItemRef && interactivePivot.range) {
                            // Interactive item is not found - let's find nearest one
                            const interactiveRange = interactivePivot.range;
                            const interactiveItemIndex = binarySearch(this.orderedItems, item => !item.range.intersectWith(interactiveRange).isEmpty || item.range.start > interactiveRange.start);
                            const interactiveItem = this.orderedItems[interactiveItemIndex];
                            interactiveItemRef = this.getItemRef(interactiveItem?.key);
                        }
                        // Debug helper
                        // interactiveItemRef.style.backgroundColor = 'red';

                        if (interactiveItemRef) {
                            const viewportTopOffset = interactivePivot.offset;
                            const isSticky = window.getComputedStyle(interactiveItemRef).position === 'sticky';
                            const interactiveItemOffset = isSticky
                                ? getOriginalPosition(interactiveItemRef)
                                : interactiveItemRef.getBoundingClientRect().top;
                            const dTopOffset = Math.floor(interactiveItemOffset - viewportTopOffset);
                            const oldContainerBottom = parseFloat(window.getComputedStyle(this.containerRef).bottom) || 0;
                            const containerBottom = -offset;
                            const dContainerBottom = containerBottom - oldContainerBottom;
                            scrollTopOffset = dTopOffset + dContainerBottom;
                            isInteractivePositioning = true;
                            debugLog?.log(`restoreScrollPosition: interactive item offset`, interactivePivot, offset, scrollTopOffset);

                            let delayedScrollTop = 0;
                            // restore scroll position with delay to prevent scroll jump, double RAF is required there
                            fastRaf(() => {
                                fastRaf({
                                    read: () => {
                                        const viewportTopOffset = interactivePivot.offset;
                                        const interactiveItemOffset = isSticky
                                            ? getOriginalPosition(interactiveItemRef)
                                            : interactiveItemRef.getBoundingClientRect().top;
                                        const dTopOffset = Math.floor(interactiveItemOffset - viewportTopOffset);
                                        const scrollTop = this.ref.scrollTop;
                                        delayedScrollTop = scrollTop + dTopOffset;
                                    },
                                    write: () => {
                                        this.ref.scrollTop = delayedScrollTop;
                                    }
                                });
                            });

                        }
                        else
                            warnLog?.log(`restoreScrollPosition: interactive item not found`, interactivePivot);
                    }

                    if (offset > 0) {
                        // scroll position does not allow to show the last item
                        scrollTopOffset = -offset;

                        // adjust item ranges
                        offset = this.resetItemRange();
                        scrollTopOffset += offset;
                    }

                    // Adjust spacer size to prevent overlap with container
                    endSpacerSize = -offset;
                    delayedEndSpacerSize = endSpacerSize;
                    if (rs.hasVeryFirstItem) {
                        spacerSize = 0;
                        delayedSpacerSize = 0;
                    }
                    else {
                        spacerSize = clamp(oldTotalSize - containerSize - endSpacerSize - endAnchorSize, 0, Infinity);
                        delayedSpacerSize = clamp(totalSize - containerSize - endSpacerSize - endAnchorSize, 0, Infinity);
                    }
                }
                else {
                    offset = start;

                    if (interactivePivot) {
                        let interactiveItemRef = this.getItemRef(interactivePivot.itemKey);
                        if (!interactiveItemRef) {
                            // Interactive item is not found - let's find nearest one
                            const interactiveRange = interactivePivot.range;
                            const interactiveItemIndex = binarySearch(this.orderedItems, item => !item.range.intersectWith(interactiveRange).isEmpty || item.range.start > interactiveRange.start);
                            const interactiveItem = this.orderedItems[interactiveItemIndex];
                            interactiveItemRef = this.getItemRef(interactiveItem?.key);
                        }
                        // Debug helper
                        // interactiveItemRef.style.backgroundColor = 'red';

                        if (interactiveItemRef) {
                            const viewportTopOffset = interactivePivot.offset;
                            const isSticky = window.getComputedStyle(interactiveItemRef).position === 'sticky';
                            const interactiveItemOffset = isSticky
                                ? getOriginalPosition(interactiveItemRef)
                                : interactiveItemRef.getBoundingClientRect().top;
                            const dTopOffset = Math.floor(interactiveItemOffset - viewportTopOffset);
                            const oldContainerTop = parseFloat(window.getComputedStyle(this.containerRef).top) || 0;
                            // noinspection UnnecessaryLocalVariableJS
                            const containerTop = offset;
                            const dContainerTop = containerTop - oldContainerTop;
                            scrollTopOffset = dTopOffset + dContainerTop;
                            isInteractivePositioning = true;
                            debugLog?.log(`restoreScrollPosition: interactive item offset`, interactivePivot, offset, scrollTopOffset);

                            let delayedScrollTop = 0;
                            // restore scroll position with delay to prevent scroll jump, double RAF is required there
                            fastRaf(() => {
                                fastRaf({
                                    read: () => {
                                        const viewportTopOffset = interactivePivot.offset;
                                        const interactiveItemOffset = isSticky
                                            ? getOriginalPosition(interactiveItemRef)
                                            : interactiveItemRef.getBoundingClientRect().top;
                                        const dTopOffset = Math.floor(interactiveItemOffset - viewportTopOffset);
                                        const scrollTop = this.ref.scrollTop;
                                        delayedScrollTop = scrollTop + dTopOffset;
                                    },
                                    write: () => {
                                        this.ref.scrollTop = delayedScrollTop;
                                    }
                                });
                            });
                        }
                        else
                            warnLog?.log(`restoreScrollPosition: interactive item not found`, interactivePivot);
                    }

                    if (offset < 0) {
                        // scroll position does not allow to show the first item
                        scrollTopOffset = offset;

                        // adjust item ranges
                        offset = this.resetItemRange();
                        scrollTopOffset -= offset;
                    }

                    // Adjust spacer size to prevent overlap with container
                    spacerSize = offset;
                    delayedSpacerSize = spacerSize;
                    if (rs.hasVeryLastItem) {
                        endSpacerSize = 0;
                        delayedEndSpacerSize = 0;
                    }
                    else {
                        endSpacerSize = clamp(oldTotalSize - containerSize - spacerSize - endAnchorSize, 0, Infinity);
                        delayedEndSpacerSize = clamp(totalSize - containerSize - spacerSize - endAnchorSize, 0, Infinity);
                    }
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
                                this.spacerRef.style.height = `${delayedSpacerSize}px`;
                                this.endSpacerRef.style.height = `${delayedEndSpacerSize}px`;
                                // console.warn(
                                //     'restoreScrollPosition: wrapper size increased with DELAY!',
                                //     totalSize);
                            }
                        }});
                    this.turnOffScrollingCallback = setWrapperHeight;

                }
                else if (totalSizeDiff != 0) {
                    this.wrapperRef.style.height = totalSize + 'px';
                    this.spacerRef.style.height = `${delayedSpacerSize}px`;
                    this.endSpacerRef.style.height = `${delayedEndSpacerSize}px`;
                }
                else {
                    this.spacerRef.style.height = `${delayedSpacerSize}px`;
                    this.endSpacerRef.style.height = `${delayedEndSpacerSize}px`;
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
                // this.updateViewportThrottled();
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

        this.viewport = null;
        // await delayAsync(50);
        // debugLog?.log(`restoreScrollPosition: end`, rafResult);
    }

    private measureItems(): void {
        if (!this.hasUnmeasuredItems)
            return;

        const unmeasuredItems = [...this.unmeasuredItems];
        let itemsWereMeasured = false;
        for (const key of unmeasuredItems) {
            const item = this.items.get(key);
            if (item && item.size < 0) {
                const itemRef = this.getItemRef(key);
                if (itemRef) {
                    const itemRect = itemRef.getBoundingClientRect();
                    const rowGap = this.rowGap;
                    const size =  Math.ceil(itemRect.height + rowGap);
                    item.size = size;
                    item.range = null;
                    this.sizeCache.set(key, size);
                } else
                    this.items.delete(key);
            }
            const hasRemoved = this.unmeasuredItems.delete(key);
            itemsWereMeasured ||= hasRemoved;
        }

        // recalculate item range as some elements were updated
        if (itemsWereMeasured)
            this.itemRange = null;
    }

    private ensureItemRangeCalculated(): boolean {
        // nothing to do when unmeasured items still exist or there were no new renders
        if (this.hasUnmeasuredItems) {
            fastRaf(() => this.measureItems());
            return false;
        }

        if (this.itemRange)
            return false;

        const { renderState: rs, orderedItems } = this;

        // nothing to do when there are no items rendered
        if (orderedItems.length == 0)
            return false;

        if (this.shouldUpdateCornerstoneItem && (rs.hasVeryFirstItem || rs.hasVeryLastItem)) {
            // We have to recalculate cornerstone item
            this.shouldUpdateCornerstoneItem = false;
            for (const item of orderedItems)
                item.range = null;
        }

        let cornerstoneItemIndex = -1;
        let cornerstoneItem: VirtualListItem = null;
        if (this.defaultEdge === VirtualListEdge.End && !rs.hasVeryLastItem) {
            cornerstoneItemIndex = orderedItems.length - 1;
            cornerstoneItem = orderedItems[cornerstoneItemIndex];
            // Find first one from the end
            while (!cornerstoneItem.range && cornerstoneItemIndex > 0) {
                cornerstoneItemIndex--;
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
            }
        }
        else if (this.defaultEdge === VirtualListEdge.Start && !rs.hasVeryFirstItem) {
            cornerstoneItemIndex = 0;
            cornerstoneItem = orderedItems[cornerstoneItemIndex];
            // Find first one from the start
            while (!cornerstoneItem.range && cornerstoneItemIndex < orderedItems.length - 1) {
                cornerstoneItemIndex++;
                cornerstoneItem = orderedItems[cornerstoneItemIndex];
            }
        }

        if (!cornerstoneItem?.range) {
            // We have checked all items and there is no cornerstone item, so let's recalculate all ranges
            this.resetItemRange(true);
        }
        else {
            // calculate range of other items
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

        // Adjust item ranges according to default edge invariant
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
        }
        else {
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

    private resetItemRange(canUseQueryRange: boolean = false): number | null {
        const { orderedItems, defaultSpacerSize, endAnchorSize, renderState: rs } = this;
        const fullRangeSize = this.knownRange?.size;

        if (orderedItems.length === 0)
            return null;

        if (this.defaultEdge === VirtualListEdge.End) {
            const cornerstoneItemIndex = orderedItems.length - 1;
            const cornerstoneItem = orderedItems[cornerstoneItemIndex];

            if (rs.beforeCount !== null && rs.afterCount !== null) {
                // We are able to calculate range based on before and after counts
                cornerstoneItem.range = new NumberRange(
                    0 - Math.floor(rs.afterCount * this.statistics.itemSize) - cornerstoneItem.size,
                    0 - Math.floor(rs.afterCount * this.statistics.itemSize));
            }
            else if (canUseQueryRange && !rs.query.isNone && !rs.hasVeryLastItem) {
                // try to reuse coords of previously rendered items
                const { virtualRange } = rs.query;
                cornerstoneItem.range = new NumberRange(
                    virtualRange.end - cornerstoneItem.size,
                    virtualRange.end);
            }
            else if (canUseQueryRange && rs.query.isNone && !rs.hasVeryLastItem) {
                // There is no query range and no very last item, so we have to calculate range manually with end spacer
                cornerstoneItem.range = new NumberRange(
                    0 - defaultSpacerSize - endAnchorSize - cornerstoneItem.size,
                    0 - defaultSpacerSize - endAnchorSize);
            }
            else if (!canUseQueryRange && !rs.hasVeryLastItem) {
                // There is no very last item, so we have to calculate range manually with end spacer
                cornerstoneItem.range = new NumberRange(
                    0 - defaultSpacerSize - endAnchorSize - cornerstoneItem.size,
                    0 - defaultSpacerSize - endAnchorSize);
            }
            else
                cornerstoneItem.range = new NumberRange(
                    0 - endAnchorSize - cornerstoneItem.size,
                    0 - endAnchorSize);

            this.shouldUpdateCornerstoneItem = !rs.hasVeryLastItem;
            let prevItem = cornerstoneItem;
            for (let i = cornerstoneItemIndex - 1; i >= 0; i--) {
                const item = orderedItems[i];
                item.range = new NumberRange(prevItem.range.start - item.size, prevItem.range.start);
                prevItem = item;
            }
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
            return cornerstoneItem.range.end;
        }
        else {
            const cornerstoneItemIndex = 0;
            const cornerstoneItem = orderedItems[cornerstoneItemIndex];

            if (rs.beforeCount !== null && rs.afterCount !== null) {
                // We are able to calculate range based on before and after counts
                cornerstoneItem.range = new NumberRange(
                    Math.floor(rs.beforeCount * this.statistics.itemSize),
                    Math.floor(rs.beforeCount * this.statistics.itemSize) + cornerstoneItem.size);
            }
            else if (canUseQueryRange && !rs.query.isNone && !rs.hasVeryFirstItem) {
                // try to reuse coords of previously rendered items
                const { virtualRange } = rs.query;
                cornerstoneItem.range = new NumberRange(
                    virtualRange.start,
                    virtualRange.start + cornerstoneItem.size);
            }
            else if (canUseQueryRange && rs.query.isNone && !rs.hasVeryFirstItem) {
                // There is no query range and no very first item, so we have to calculate range manually with spacer
                cornerstoneItem.range = new NumberRange(
                    defaultSpacerSize,
                    defaultSpacerSize + cornerstoneItem.size);
            }
            else if (!canUseQueryRange && !rs.hasVeryFirstItem) {
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

            let prevItem = cornerstoneItem;
            for (let i = cornerstoneItemIndex + 1; i < orderedItems.length; i++) {
                const item = orderedItems[i];
                item.range = new NumberRange(prevItem.range.end, prevItem.range.end + item.size);
                prevItem = item;
            }
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
            return cornerstoneItem.range.start;
        }
    }

    private async requestData(): Promise<void> {
        if (this.isRendering || !this.viewport || !this.itemRange)
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
        const itemRange = this.itemRange;
        const queryRange = query.virtualRange;
        const viewport = this.viewport;
        const rs = this.renderState;
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
        const responseFulfillmentRatio = rs.beforeCount !== null && rs.afterCount !== null
            ? 1 // We know count precisely
            : this.statistics.responseFulfillmentRatio;
        const viewport = this.viewport;
        const alreadyLoaded = this.itemRange;
        if (!viewport || !alreadyLoaded)
            return this.lastQuery;

        if (this.hasUnmeasuredItems) { // Let's wait for measurement to complete first
            fastRaf(() => this.measureItems());
            return this.lastQuery;
        }
        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return this.lastQuery; // We have already loaded all data

        if (this.isRendering)
            return this.lastQuery; // Do not request data during rendering as it might be inconsistent

        const now = Date.now();
        if (now - this.renderCompletedAt < 500 && this.lastQuery.isNone)
            return this.lastQuery; // Do not request data during the first second after render caused by updated data (not scroll)
        //
        // if (now - this.renderCompletedAt < UpdateViewportInterval)
        //     return this.lastQuery; // Do not request data too often

        const viewportSize = viewport.size;
        const lastQuerySide = this.lastQuery.moveRange.isEmpty
            ? 'none'
            : (this.lastQuery.moveRange.start >= 0 && this.lastQuery.moveRange.end >= 0
                ? 'end'
                : 'start');
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
        const orderedItems = [...this.orderedItems];
        if (orderedItems.length == 0) // No entries -> nothing to "align" the query to
            return this.lastQuery;

        if (alreadyLoaded.contains(loadZone)) {
            // debug helper
            // console.warn('already!', viewport, alreadyLoaded, loadZone);
            return this.lastQuery;
        }

        if (orderedItems.some(item => item.range == null)) {
            this.itemRange = null;
            this.ensureItemRangeCalculated();
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

        let moveRangeStart = Math.ceil((loadZone.start - firstItem.range.start) / itemSize / responseFulfillmentRatio);
        let moveRangeEnd = Math.ceil((loadZone.end - lastItem.range.end) / itemSize / responseFulfillmentRatio);
        // Adjust moveRange based on default edge and viewport proximity
        if (this.defaultEdge === VirtualListEdge.End) {
            const isNearEnd = viewport.end >= this.itemRange.end - viewportSize * 0.1;
            if (isNearEnd)
                moveRangeStart = Math.max(moveRangeStart, 0); // Prevent loading items before
        } else if (this.defaultEdge === VirtualListEdge.Start) {
            const isNearStart = viewport.start <= this.itemRange.start + viewportSize * 0.1;
            if (isNearStart)
                moveRangeEnd = Math.min(moveRangeEnd, 0); // Prevent loading items after
        }
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
