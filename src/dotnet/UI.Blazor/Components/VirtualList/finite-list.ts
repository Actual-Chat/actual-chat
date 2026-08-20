import { throttle } from 'actuallab-core';
import { DotNet } from '@microsoft/dotnet-js-interop';
import { getLogs } from 'logging';
import { NumberRange, Range } from './ts/range';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualList } from './virtual-list';
import { isSpacerShown, VirtualListOverlayStats } from './virtual-list-overlay';

const { debugLog } = getLogs('FiniteList');

const UpdateViewportIntervalMs = 64;
const UpdateItemVisibilityIntervalMs = 250;
const DefaultItemSize = 48;
// Below this the spacers would be rewritten for sub-pixel noise, and every spacer rewrite is a
// chance for the browser to clamp scrollTop.
const ItemSizeEpsilon = 0.5;
// indexAt removes the separators above its guess; one pass per separator in the way is enough.
const IndexAtMaxPasses = 4;

// A virtualized list of a known length whose items are all one height, save for a few irregular
// ones. Item position is a pure function of index, so a window move alone cannot shift what is on
// screen - only a change of the measured item size or an irregular item entering above the viewport can.
export class FiniteList extends VirtualList {
    private readonly sizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private readonly visibleKeys = new Set<string>();

    private itemSize = DefaultItemSize;
    // The extra height an item carrying a block separator has over a regular one, and where in the
    // whole list those items are. Together they make a position an exact function of index again.
    private separatorSize = 0;
    private separatorIndexes = new Array<number>();

    public static create(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        expandMultiplier: number,
    ): FiniteList {
        return new FiniteList(ref, backendRef, identity, expandMultiplier);
    }

    public constructor(
        ref: HTMLElement,
        backendRef: DotNet.DotNetObject,
        identity: string,
        private readonly expandMultiplier: number,
    ) {
        super(ref, backendRef, identity);
        const listenerOptions = { passive: true, signal: this.abortController.signal };
        ref.addEventListener('scroll', this.onScroll, listenerOptions);
        this.sizeObserver = new ResizeObserver(() => this.updateViewportThrottled());
        this.sizeObserver.observe(ref);
        this.visibilityObserver = new IntersectionObserver(this.onItemVisibilityChanged, { root: ref });
        this.start();
    }

    public override dispose(): void {
        super.dispose();
        this.sizeObserver.disconnect();
        this.visibilityObserver.disconnect();
    }

    public getOverlayStats(): VirtualListOverlayStats {
        const rs = this.renderState;
        const beforeCount = rs.beforeCount ?? 0;
        const total = beforeCount + rs.count + (rs.afterCount ?? 0);
        return {
            renderDirection: 'down',
            stickyEdge: null,
            hasStartSpacer: isSpacerShown(this.spacerRef),
            hasEndSpacer: isSpacerShown(this.endSpacerRef),
            hasVeryFirstItem: rs.hasVeryFirstItem,
            hasVeryLastItem: rs.hasVeryLastItem,
            isRequestingData: this.isRequestingData,
            isRendering: false,
            lastDataRequestAt: this.lastDataRequestAt,
            lastRenderAt: this.lastRenderAt,
            window: `${this.visibleKeys.size}(${beforeCount}-${beforeCount + rs.count})`,
            total,
            meanItemHeight: this.itemSize,
        };
    }

    // Protected methods

    protected onRender(rs: VirtualListRenderState): void {
        this.observeItems();
        this.measureItemSize();
        this.measureSeparatorSize();
        this.separatorIndexes = rs.separatorIndexes ?? [];

        const beforeCount = rs.beforeCount ?? 0;
        const itemCount = beforeCount + rs.count + (rs.afterCount ?? 0);
        // Both spacers stand for items nobody has rendered, so their heights come from the model - and
        // the model now counts the separators among them. Sitting in the same flex column as the items,
        // each also contributes a row gap that the position it stands for does not.
        this.setSpacerSize(this.spacerRef, this.topOf(beforeCount));
        this.setSpacerSize(this.endSpacerRef, this.topOf(itemCount) - this.topOf(beforeCount + rs.count));
        // The only scroll this list ever writes: an explicit request to show a given item. A window
        // move cannot shift what is on screen - position is a function of index - so nothing else has
        // anything to correct, and a correction here would be indistinguishable from the user's own
        // scroll and would end their fling.
        if (rs.scrollToKey)
            this.scrollToKey(rs.scrollToKey, rs.scrollToKeyInTheMiddle ?? false);

        this.reveal();
        void this.requestData();
    }

    // Wrapper coordinates of the item at `index`, counting the separators that come before it.
    private topOf(index: number): number {
        return index * this.itemSize + this.separatorsBefore(index) * this.separatorSize;
    }

    // How many separators sit above `index`. A separator index is the item it follows, so an index
    // equal to it is still below the separator.
    private separatorsBefore(index: number): number {
        const indexes = this.separatorIndexes;
        let low = 0;
        let high = indexes.length;
        while (low < high) {
            const mid = (low + high) >> 1;
            if (indexes[mid] < index)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

    // The inverse of topOf: the index whose row contains `offset`.
    private indexAt(offset: number): number {
        if (this.itemSize <= 0)
            return 0;

        let index = Math.floor(offset / this.itemSize);
        // Each separator above pushes the answer earlier, and removing them can never uncover another
        // one below - so this converges in as many steps as there are separators in the way, i.e. one
        // for the chat list.
        for (let pass = 0; pass < IndexAtMaxPasses; pass++) {
            const next = Math.floor((offset - this.separatorsBefore(index) * this.separatorSize) / this.itemSize);
            if (next === index)
                break;

            index = next;
        }
        return Math.max(0, index);
    }

    protected buildDataQuery(): VirtualListDataQuery | null {
        const rs = this.renderState;
        if (rs.count === 0 || (rs.hasVeryFirstItem && rs.hasVeryLastItem))
            return null;

        // A hidden list has no viewport to load around, and the zone that falls out of a zero height
        // asks for index 0 - which would walk the window to the top behind the user's back.
        const clientHeight = this.ref.clientHeight;
        if (clientHeight <= 0)
            return null;

        const beforeCount = rs.beforeCount ?? 0;
        const afterCount = rs.afterCount ?? 0;
        const itemCount = beforeCount + rs.count + afterCount;
        const loadedRange = new NumberRange(beforeCount, beforeCount + rs.count);
        const viewportItemCount = clientHeight / this.itemSize;
        const firstVisible = this.indexAt(this.ref.scrollTop);
        const zone = Math.ceil(viewportItemCount * this.expandMultiplier);
        const wantStart = Math.max(0, firstVisible - zone);
        const wantEnd = Math.min(itemCount, Math.ceil(firstVisible + viewportItemCount) + zone);
        if (wantStart >= loadedRange.start && wantEnd <= loadedRange.end)
            return null;

        const moveRange = new NumberRange(wantStart - loadedRange.start, wantEnd - loadedRange.end);
        if (moveRange.start === 0 && moveRange.end === 0)
            return null;

        const query = new VirtualListDataQuery(
            new Range<string>(rs.keyRange.start, rs.keyRange.end),
            new NumberRange(wantStart, wantEnd),
            moveRange);
        query.expectedCount = wantEnd - wantStart;
        return query;
    }

    // Private methods

    // The size source is any regular item; irregular ones (block separators) carry extra height and
    // would poison the estimate. Sticking to one key while it stays rendered keeps the value from
    // flapping between two rows that differ by a sub-pixel.
    private measureItemSize(): void {
        const sizeSource = this.containerRef.querySelector<HTMLElement>(':scope > li.item[data-vl-size-source]');
        if (!sizeSource)
            return;

        const size = sizeSource.getBoundingClientRect().height + this.rowGap;
        if (size <= 0 || Math.abs(size - this.itemSize) < ItemSizeEpsilon)
            return;

        debugLog?.log(`[${this.identity}] measureItemSize: ${this.itemSize} -> ${size}`);
        this.itemSize = size;
    }

    private setSpacerSize(spacerRef: HTMLElement, size: number): void {
        const height = size > 0 ? Math.max(0, size - this.rowGap) : 0;
        const display = size > 0 ? 'flex' : 'none';
        // Written only when it changes: on WebKit every content-height change of a scroller is a chance
        // to end a fling, and both spacers are rewritten on every render.
        if (spacerRef.style.display !== display)
            spacerRef.style.display = display;
        if (parseFloat(spacerRef.style.height) !== height)
            spacerRef.style.height = `${height}px`;
    }

    // The separator is rendered once off-layout so it can be measured even when every item carrying
    // one is outside the loaded window. Its margins are part of what it costs, and they are not in
    // the element's own box.
    private measureSeparatorSize(): void {
        const measureRef = this.containerRef
            .querySelector<HTMLElement>(':scope > .c-separator-measure > *');
        if (measureRef == null)
            return;

        const style = getComputedStyle(measureRef);
        const size = measureRef.getBoundingClientRect().height
            + (parseFloat(style.marginTop) || 0)
            + (parseFloat(style.marginBottom) || 0);
        if (size < 0 || Math.abs(size - this.separatorSize) < ItemSizeEpsilon)
            return;

        debugLog?.log(`[${this.identity}] measureSeparatorSize: ${this.separatorSize} -> ${size}`);
        this.separatorSize = size;
    }

    private scrollToKey(key: string, isInTheMiddle: boolean): void {
        const itemRef = this.getItemRef(key);
        if (!itemRef)
            return;

        const offset = itemRef.getBoundingClientRect().top - this.ref.getBoundingClientRect().top;
        this.ref.scrollTop += isInTheMiddle
            ? offset - (this.ref.clientHeight - this.itemSize) / 2
            : offset;
    }

    private onScroll = (): void => {
        if (this.isDisposed)
            return;

        this.updateViewportThrottled();
    };

    private readonly updateViewportThrottled = throttle(
        () => void this.requestData(),
        UpdateViewportIntervalMs,
        'default');

    private onItemVisibilityChanged = (entries: IntersectionObserverEntry[]): void => {
        for (const entry of entries) {
            const key = (entry.target as HTMLElement).dataset.key;
            if (!key)
                continue;

            if (entry.isIntersecting)
                this.visibleKeys.add(key);
            else
                this.visibleKeys.delete(key);
        }

        this.updateItemVisibilityThrottled();
    };

    private readonly updateItemVisibilityThrottled = throttle(
        () => this.updateItemVisibility(),
        UpdateItemVisibilityIntervalMs,
        'default');

    private updateItemVisibility(): void {
        const isEndAnchorVisible = this.renderState.hasVeryLastItem
            && this.ref.scrollTop + this.ref.clientHeight >= this.ref.scrollHeight - 1;
        void this.reportVisibility([...this.visibleKeys], isEndAnchorVisible);
    }

    // Keys that left the DOM are dropped, but the rest keep their state: clearing the whole set here
    // and re-observing would report an empty viewport on every render, which reads downstream as "the
    // user can see no chats at all".
    private observeItems(): void {
        this.visibilityObserver.disconnect();
        const itemRefs = this.itemRefs();
        const keys = new Set(itemRefs.map(x => x.dataset.key!));
        let hasDeparted = false;
        for (const key of [...this.visibleKeys])
            if (!keys.has(key)) {
                this.visibleKeys.delete(key);
                hasDeparted = true;
            }
        for (const itemRef of itemRefs)
            this.visibilityObserver.observe(itemRef);

        // Nothing else will report a departure: an intersection callback only arrives for a node that
        // is still observed, so a render that drops every item - a filter that now matches nothing -
        // would leave the last report standing and the consumer acting on keys that are gone.
        if (hasDeparted)
            this.updateItemVisibilityThrottled();
    }

    private itemRefs(): HTMLElement[] {
        return [...this.containerRef.querySelectorAll<HTMLElement>(':scope > li.item[data-key]')];
    }
}

(globalThis as unknown as { FiniteList: typeof FiniteList }).FiniteList = FiniteList;
