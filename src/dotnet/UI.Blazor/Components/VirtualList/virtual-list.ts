import { delayAsync, PromiseSourceWithTimeout } from 'actuallab-core';
import { DotNet } from '@microsoft/dotnet-js-interop';
import { getLogs } from 'logging';
import { VirtualListDataQuery } from './ts/virtual-list-data-query';
import { VirtualListRenderState } from './ts/virtual-list-render-state';
import { Range } from './ts/range';
import { VirtualListOverlay, VirtualListOverlayStats, VirtualListOverlayTarget } from './virtual-list-overlay';
import { ContentSwap } from '../ContentSwap/content-swap';

const { warnLog } = getLogs('VirtualList');

// Debug aid: ?vlloaddelay=<ms> holds every data load for that long. A fast fling into
// history is only interesting while the loads can't keep up with it, and that state is otherwise a race
// to catch.
const LoadDelayMs = readLoadDelay();

// Only fires when a request never comes back at all - endRender and renderSkipped cover both normal
// outcomes - so it is a fault backstop, not flow control.
const RequestDataTimeoutMs = 2500;
// A request that died took the only thing that was going to produce a render with it, so nothing else
// will ask again until the user moves. This is how a list that is sitting on a skeleton gets unstuck.
const RequestDataRetryMs = 1000;

// Set by VirtualList.IsContentSwapDependency: this list is what its enclosing swap area waits for.
const ContentSwapDependencyAttribute = 'data-content-swap-dependency';

// VirtualListDataQuery.None is identified by reference, and a query that arrived as JSON is a plain
// object with no prototype - so `isNone` on one is always undefined. Compare the ranges instead.
export function isNoneQuery(query: VirtualListDataQuery): boolean {
    return query.keyRange.start === '' && query.keyRange.end === ''
        && query.moveRange.start === 0 && query.moveRange.end === 0;
}

export const EmptyRenderState: VirtualListRenderState = {
    renderIndex: -1,
    query: VirtualListDataQuery.None,
    keyRange: new Range<string>('', ''),
    beforeCount: null,
    afterCount: null,
    estimatedCount: null,
    count: 0,
    hasVeryFirstItem: false,
    hasVeryLastItem: false,
};

// What both virtualized lists share: the Blazor round trip (render in, data query out, item
// visibility back), the DOM handles, and the initial reveal. All geometry belongs to the derived
// class - see InfiniteList for the unbounded anchored case and FiniteList for the index-positioned one.
export abstract class VirtualList implements VirtualListOverlayTarget {
    protected readonly wrapperRef: HTMLElement;
    protected readonly containerRef: HTMLElement;
    protected readonly spacerRef: HTMLElement;
    protected readonly endSpacerRef: HTMLElement;
    protected readonly renderStateRef: HTMLElement;
    protected readonly renderIndexRef: HTMLElement;
    protected readonly abortController = new AbortController();
    protected readonly rowGap: number;
    protected readonly renderObserver: MutationObserver;
    protected overlay: VirtualListOverlay | null = null;

    protected renderState: VirtualListRenderState = EmptyRenderState;
    // What was last asked for, so a list can tell a new query from one it has already sent.
    protected lastSentQuery: VirtualListDataQuery | null = null;
    protected isDisposed = false;
    protected lastDataRequestAt: number | null = null;
    protected lastRenderAt: number | null = null;

    private isRevealed = false;
    private isContentSwapDisplayed = false;
    private whenRequestDataCompleted: PromiseSourceWithTimeout<void> | null = null;

    protected constructor(
        public readonly ref: HTMLElement,
        protected readonly blazorRef: DotNet.DotNetObject,
        protected readonly identity: string,
    ) {
        this.wrapperRef = ref.querySelector(':scope > .c-wrapper')!;
        this.containerRef = this.wrapperRef.querySelector(':scope > .c-virtual-container')!;
        this.spacerRef = this.containerRef.querySelector(':scope > .c-spacer-start')!;
        this.endSpacerRef = this.containerRef.querySelector(':scope > .c-spacer-end')!;
        this.renderStateRef = ref.querySelector(':scope > .data.render-state')!;
        this.renderIndexRef = ref.querySelector(':scope > .data.render-index')!;
        this.rowGap = parseFloat(window.getComputedStyle(this.containerRef).rowGap) || 0;

        // Both triggers, because Blazor applies a render in several batches and the render-state JSON
        // is deliberately written in the last one (RenderAtDepth) - the items and the JSON can land in
        // different batches, and only the JSON's own render index says the render is complete.
        this.renderObserver = new MutationObserver(this.onRenderBatch);
        this.renderObserver.observe(this.renderIndexRef, { attributes: true });
        this.renderObserver.observe(this.containerRef, { childList: true, subtree: true });
    }

    // Called by blazor
    public dispose(): void {
        this.isDisposed = true;
        this.overlay?.dispose();
        this.renderObserver.disconnect();
        this.abortController.abort();
        this.releaseRequestDataGuard();
    }

    // Called by blazor when the new data turned out to be identical to the rendered one
    public renderSkipped(): void {
        this.releaseRequestDataGuard();
        // No render means nothing else will re-evaluate the query; a list sitting on a skeleton would
        // otherwise wait there until the user moved.
        setTimeout(() => void this.requestData(), RequestDataRetryMs);
    }

    public abstract getOverlayStats(): VirtualListOverlayStats;

    // Protected methods

    protected abstract onRender(rs: VirtualListRenderState): void;
    protected abstract buildDataQuery(): VirtualListDataQuery | null;

    protected get isRequestingData(): boolean {
        return this.whenRequestDataCompleted?.isCompleted === false;
    }

    protected async requestData(): Promise<void> {
        if (this.isDisposed)
            return;

        const query = this.buildDataQuery();
        if (query == null || isNoneQuery(query))
            return;

        if (this.isRequestingData)
            return;

        const whenCompleted = new PromiseSourceWithTimeout<void>();
        whenCompleted.setTimeout(RequestDataTimeoutMs, () => {
            warnLog?.log(`[${this.identity}] requestData: no render within ${RequestDataTimeoutMs}ms`);
            whenCompleted.resolve(undefined);
            this.retryRequestData();
        });
        this.whenRequestDataCompleted = whenCompleted;
        this.lastDataRequestAt = performance.now();
        this.lastSentQuery = query;
        try {
            if (LoadDelayMs > 0)
                await delayAsync(LoadDelayMs);

            await this.blazorRef.invokeMethodAsync('RequestData', query);
        }
        catch (e) {
            warnLog?.log(`[${this.identity}] requestData: failed`, e);
            this.releaseRequestDataGuard();
            this.retryRequestData();
        }
    }

    protected async reportVisibility(
        visibleKeys: string[], isEndAnchorVisible: boolean, isPinnedToEnd = isEndAnchorVisible): Promise<void> {
        if (this.isDisposed)
            return;

        try {
            await this.blazorRef.invokeMethodAsync(
                'UpdateItemVisibility', this.identity, visibleKeys, isEndAnchorVisible, isPinnedToEnd);
        }
        catch (e) {
            // DisposeAsync drops BlazorRef right after the JS dispose(), so a call that passed the
            // isDisposed check above can still land on a disposed reference - the check can't span
            // the await, and an unhandled rejection here trips the error barrier.
            warnLog?.log(`[${this.identity}] reportVisibility: failed`, e);
        }
    }

    // Called at the end of each derived constructor, once that class's own fields exist - the overlay
    // reads them straight away through getOverlayStats. It also picks up the render Blazor had already
    // applied before it created the JS side, which the observer therefore never saw.
    protected start(): void {
        this.overlay = new VirtualListOverlay(this);
        this.onRenderBatch();
    }

    protected reveal(): void {
        if (this.isRevealed)
            return;

        this.isRevealed = true;
        // Inline beats the c-initially-hidden class, so later renders keeping the class stay visible.
        this.wrapperRef.style.visibility = 'visible';
    }

    // Called by a derived list once its content is on screen, which is later than reveal() for a
    // FiniteList: that one un-hides on its very first render, skeletons included.
    protected displayContentSwap(): void {
        if (this.isContentSwapDisplayed || !this.ref.hasAttribute(ContentSwapDependencyAttribute))
            return;

        this.isContentSwapDisplayed = true;
        ContentSwap.display(this.ref);
    }

    protected get isContainerRevealed(): boolean {
        return this.isRevealed;
    }

    protected getItemRef(key: string): HTMLElement | null {
        return this.containerRef.querySelector<HTMLElement>(`:scope .item[data-key="${CSS.escape(key)}"]`);
    }

    protected releaseRequestDataGuard(): void {
        this.whenRequestDataCompleted?.resolve(undefined);
        this.whenRequestDataCompleted = null;
    }

    // Private methods

    private retryRequestData(): void {
        this.lastSentQuery = null;
        setTimeout(() => void this.requestData(), RequestDataRetryMs);
    }

    private onRenderBatch = (): void => {
        if (this.isDisposed)
            return;

        const rs = this.parseRenderState();
        if (rs == null)
            return;

        this.renderState = rs;
        this.lastRenderAt = performance.now();
        // Released before the render, not after: the render IS the completion of the request that
        // asked for it, and onRender asks for the next window - which the still-held guard would drop.
        this.releaseRequestDataGuard();
        this.onRender(rs);
    };

    private parseRenderState(): VirtualListRenderState | null {
        const rsJson = this.renderStateRef.textContent;
        if (!rsJson)
            return null;

        // The attribute is written in the first batch and the JSON in the last, so they agree only
        // once the whole render has been applied. Checked first because it costs an attribute read
        // rather than a JSON.parse, and this runs on every mutation inside the list.
        const renderIndex = Number.parseInt(this.renderIndexRef.dataset.renderIndex ?? '', 10);
        if (!Number.isFinite(renderIndex) || renderIndex <= this.renderState.renderIndex)
            return null;

        try {
            const rs = JSON.parse(rsJson) as VirtualListRenderState;
            return rs.renderIndex === renderIndex ? rs : null;
        }
        catch (e) {
            warnLog?.log(`[${this.identity}] parseRenderState: failed`, e);
            return null;
        }
    }
}

function readLoadDelay(): number {
    const value = new URLSearchParams(location.search).get('vlloaddelay');
    if (value == null)
        return 0;

    const result = Number(value);
    return Number.isFinite(result) && result > 0 ? result : 0;
}
