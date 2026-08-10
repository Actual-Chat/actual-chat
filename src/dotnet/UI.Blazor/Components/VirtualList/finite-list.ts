import { PromiseSourceWithTimeout, throttle } from 'actuallab-core';
import { NumberRange, Range } from './ts/range';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { getLogs } from 'logging';
import { fastRaf } from 'fast-raf';
import { DotNet } from '@microsoft/dotnet-js-interop';

const { warnLog, debugLog } = getLogs('FiniteList');

const UpdateViewportInterval = 64;
const UpdateItemVisibilityInterval = 250;
// Only ever fires when a request never comes back at all - endRender/renderSkipped cover both
// normal outcomes - so it is a fault backstop, not flow control. Firing it is worth a warning.
const RequestDataTimeout = 2500;
const DefaultItemSize = 48;
// Republish threshold for the measured item size: below this the spacers would be rewritten for
// sub-pixel noise, and every spacer rewrite is a chance for the browser to clamp scrollTop.
const ItemSizeEpsilon = 0.5;
const AnchorEpsilon = 0.5;

const delayMs = (ms: number): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

/// A virtualized list of a known length whose items are all the same height, save for a few
/// irregular ones (separators). Item position is a pure function of index, so loading a different
/// window cannot move what is already on screen. See InfiniteList for the unbounded, anchored case.
export class FiniteList {
    // Debug-only: when > 0, inject a random [0, value) ms delay before each data request to
    // simulate uncached data and surface scroll/data timing races. 0 in prod.
    public static debugDataLoadDelayMs = 0;
    private static readonly _instances = new Set<FiniteList>();

    public static getInstances(): FiniteList[] {
        return [...FiniteList._instances];
    }

    private readonly ref: HTMLElement;
    private readonly wrapperRef: HTMLElement;
    private readonly containerRef: HTMLElement;
    private readonly spacerRef: HTMLElement;
    private readonly endSpacerRef: HTMLElement;
    private readonly renderStateRef: HTMLElement;
    private readonly renderIndexRef: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly identity: string;
    private readonly expandMultiplier: number;
    private readonly abortController: AbortController;
    private readonly renderObserver: MutationObserver;
    private readonly sizeObserver: ResizeObserver;
    private readonly visibilityObserver: IntersectionObserver;
    private readonly visibleKeys = new Set<string>();
    private readonly rowGap: number;

    private isDisposed = false;
    private isRevealed = false;
    private lastRenderIndex = -1;
    private itemSize = DefaultItemSize;
    private renderState: VirtualListRenderState | null = null;
    private whenRequestDataCompleted: PromiseSourceWithTimeout<void> | null = null;
    private scrollAnchor: { key: string; offset: number } | null = null;

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
        expandMultiplier: number,
    ) {
        globalThis['FiniteList'] = FiniteList;
        this.ref = ref;
        this.blazorRef = backendRef;
        this.identity = identity;
        this.expandMultiplier = expandMultiplier;

        this.wrapperRef = this.ref.querySelector(':scope > .c-wrapper')!;
        this.containerRef = this.wrapperRef.querySelector(':scope > .c-virtual-container')!;
        this.spacerRef = this.containerRef.querySelector(':scope > .c-spacer-start')!;
        this.endSpacerRef = this.containerRef.querySelector(':scope > .c-spacer-end')!;
        this.renderStateRef = this.ref.querySelector(':scope > .data.render-state')!;
        this.renderIndexRef = this.ref.querySelector(':scope > .data.render-index')!;
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;

        this.abortController = new AbortController();
        const listenerOptions = { signal: this.abortController.signal, passive: true };
        this.ref.addEventListener('scroll', this.onScroll, listenerOptions);

        // Both triggers, because a render arrives in several batches: the items and the render-state
        // JSON can land in different ones. endRender gates on the JSON's render index.
        this.renderObserver = new MutationObserver(this.onRenderIndexChanged);
        this.renderObserver.observe(this.renderIndexRef, { attributes: true });
        this.renderObserver.observe(this.containerRef, { childList: true });
        this.sizeObserver = new ResizeObserver(this.onResize);
        this.sizeObserver.observe(this.ref);
        this.visibilityObserver = new IntersectionObserver(this.onItemVisibilityChanged, { root: this.ref });

        FiniteList._instances.add(this);
        this.endRender();
    }

    /** Called by blazor */
    public dispose(): void {
        this.isDisposed = true;
        FiniteList._instances.delete(this);
        this.abortController.abort();
        this.renderObserver.disconnect();
        this.sizeObserver.disconnect();
        this.visibilityObserver.disconnect();
        this.releaseRequestDataGuard();
    }

    /** Called by blazor */
    public reset(): void {
        this.renderState = null;
        this.lastRenderIndex = -1;
        this.scrollAnchor = null;
        this.visibleKeys.clear();
        this.isRevealed = false;
        this.wrapperRef.style.visibility = '';
        this.releaseRequestDataGuard();
    }

    /** Called by blazor when the new data turned out to be identical to the rendered one */
    public renderSkipped(): void {
        this.releaseRequestDataGuard();
    }

    // Private methods

    private endRender(): void {
        const rs = this.parseRenderState();
        // Blazor applies a render in several batches and the render-state JSON is deliberately
        // written in the last one (RenderAtDepth), so an earlier trigger sees the new items next to
        // the previous state. Laying spacers out against that pair moves every item below them by
        // the whole spacer delta, so wait for the JSON's own render index to advance.
        if (rs === null || rs.renderIndex <= this.lastRenderIndex)
            return;

        this.lastRenderIndex = rs.renderIndex;
        this.renderState = rs;
        this.observeItems();
        fastRaf({
            key: `finiteList_${this.identity}`,
            read: () => {
                this.measureItemSize();
                this.captureAnchor();
            },
            write: () => {
                this.layout(rs);
                if (rs.scrollToKey)
                    this.scrollToKey(rs.scrollToKey, rs.scrollToKeyInTheMiddle ?? false);
                else
                    this.restoreAnchor();
                this.reveal();
                this.releaseRequestDataGuard();
                void this.requestData();
            },
        });
    }

    // The size source is any regular item; irregular ones (separators) carry extra height and would
    // poison the estimate. Sticking to one key while it stays rendered keeps the value from flapping
    // between two rows that differ by a sub-pixel.
    private measureItemSize(): void {
        const sizeSource = this.containerRef.querySelector<HTMLElement>(':scope > li.item[data-vl-size-source]');
        if (!sizeSource)
            return;

        const size = sizeSource.getBoundingClientRect().height + this.rowGap;
        if (size <= 0 || Math.abs(size - this.itemSize) < ItemSizeEpsilon)
            return;

        debugLog?.log(`measureItemSize: ${this.itemSize} -> ${size}`);
        this.itemSize = size;
    }

    private layout(rs: VirtualListRenderState): void {
        const beforeCount = rs.beforeCount ?? 0;
        const afterCount = rs.afterCount ?? 0;
        this.setSpacerSize(this.spacerRef, beforeCount * this.itemSize);
        this.setSpacerSize(this.endSpacerRef, afterCount * this.itemSize);
    }

    private setSpacerSize(spacerRef: HTMLElement, size: number): void {
        spacerRef.style.height = `${size}px`;
        spacerRef.style.display = size > 0 ? 'flex' : 'none';
    }

    private captureAnchor(): void {
        const viewportTop = this.ref.getBoundingClientRect().top;
        for (const itemRef of this.itemRefs()) {
            const rect = itemRef.getBoundingClientRect();
            if (rect.bottom <= viewportTop)
                continue;

            this.scrollAnchor = { key: itemRef.dataset.key!, offset: rect.top - viewportTop };
            return;
        }
        this.scrollAnchor = null;
    }

    // Items keep their position across renders by construction, but an irregular row entering the
    // loaded window above the viewport - or the one-off itemSize change - still shifts content.
    private restoreAnchor(): void {
        const anchor = this.scrollAnchor;
        if (!anchor)
            return;

        const itemRef = this.getItemRef(anchor.key);
        if (!itemRef)
            return;

        const viewportTop = this.ref.getBoundingClientRect().top;
        const delta = itemRef.getBoundingClientRect().top - viewportTop - anchor.offset;
        if (Math.abs(delta) < AnchorEpsilon)
            return;

        debugLog?.log(`restoreAnchor: ${delta}`);
        this.ref.scrollTop += delta;
    }

    private scrollToKey(key: string, inTheMiddle: boolean): void {
        const itemRef = this.getItemRef(key);
        if (!itemRef)
            return;

        const viewportTop = this.ref.getBoundingClientRect().top;
        const offset = itemRef.getBoundingClientRect().top - viewportTop;
        const target = inTheMiddle
            ? offset - (this.ref.clientHeight - this.itemSize) / 2
            : offset;
        this.ref.scrollTop += target;
        this.captureAnchor();
    }

    private reveal(): void {
        if (this.isRevealed)
            return;

        this.isRevealed = true;
        this.wrapperRef.style.visibility = 'visible';
    }

    private onRenderIndexChanged = (): void => {
        if (!this.isDisposed)
            this.endRender();
    };

    private onScroll = (): void => {
        if (!this.isDisposed)
            this.updateViewportThrottled();
    };

    private onResize = (): void => {
        if (!this.isDisposed)
            this.updateViewportThrottled();
    };

    private readonly updateViewportThrottled = throttle(
        () => this.updateViewport(),
        UpdateViewportInterval,
        'default');

    private updateViewport(): void {
        void this.requestData();
    }

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
        UpdateItemVisibilityInterval,
        'default');

    private updateItemVisibility(): void {
        if (this.isDisposed)
            return;

        const rs = this.renderState;
        const isEndAnchorVisible = rs != null
            && rs.hasVeryLastItem
            && this.ref.scrollTop + this.ref.clientHeight >= this.ref.scrollHeight - 1;
        void this.blazorRef.invokeMethodAsync(
            'UpdateItemVisibility', this.identity, [...this.visibleKeys], isEndAnchorVisible);
    }

    private observeItems(): void {
        this.visibilityObserver.disconnect();
        this.visibleKeys.clear();
        for (const itemRef of this.itemRefs())
            this.visibilityObserver.observe(itemRef);
    }

    private async requestData(): Promise<void> {
        if (this.isDisposed)
            return;

        const query = this.buildDataQuery();
        if (query === null)
            return;

        const whenRequestDataCompleted = this.whenRequestDataCompleted;
        if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted) {
            debugLog?.log(`requestData: the previous request is not completed yet`);
            return;
        }

        const newWhenRequestDataCompleted = new PromiseSourceWithTimeout<void>();
        newWhenRequestDataCompleted.setTimeout(RequestDataTimeout, () => {
            warnLog?.log(`requestData: no render within ${RequestDataTimeout}ms - the request never came back`);
            newWhenRequestDataCompleted.resolve(undefined);
        });
        this.whenRequestDataCompleted = newWhenRequestDataCompleted;
        debugLog?.log(`requestData:`, query);
        if (FiniteList.debugDataLoadDelayMs > 0)
            await delayMs(Math.random() * FiniteList.debugDataLoadDelayMs);

        await this.blazorRef.invokeMethodAsync('RequestData', query);
    }

    private buildDataQuery(): VirtualListDataQuery | null {
        const rs = this.renderState;
        if (!rs || rs.count === 0)
            return null;
        if (rs.hasVeryFirstItem && rs.hasVeryLastItem)
            return null;

        const beforeCount = rs.beforeCount ?? 0;
        const afterCount = rs.afterCount ?? 0;
        const itemCount = beforeCount + rs.count + afterCount;
        const loadedRange = new NumberRange(beforeCount, beforeCount + rs.count);
        const viewportItemCount = this.ref.clientHeight / this.itemSize;
        const firstVisible = Math.floor(this.ref.scrollTop / this.itemSize);
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

    private releaseRequestDataGuard(): void {
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
    }

    private parseRenderState(): VirtualListRenderState | null {
        const rsJson = this.renderStateRef.textContent;
        if (!rsJson)
            return null;

        try {
            return JSON.parse(rsJson) as VirtualListRenderState;
        }
        catch (error) {
            warnLog?.log(`parseRenderState: failed to parse render state`, error);
            return null;
        }
    }

    private itemRefs(): HTMLElement[] {
        return [...this.containerRef.querySelectorAll<HTMLElement>(':scope > li.item[data-key]')];
    }

    private getItemRef(key: string): HTMLElement | null {
        return this.containerRef.querySelector<HTMLElement>(`:scope > li.item[data-key="${CSS.escape(key)}"]`);
    }
}
