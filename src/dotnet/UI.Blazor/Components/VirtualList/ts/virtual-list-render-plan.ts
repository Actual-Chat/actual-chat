import { VirtualListItem } from './virtual-list-item';
import { ItemRenderPlan } from './item-render-plan';
import { Range } from './range';
import { VirtualListEdge } from './virtual-list-edge';
import { VirtualListData } from './virtual-list-data';
import { VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListStickyEdgeState } from './virtual-list-sticky-edge-state';
import { VirtualListStatistics } from './virtual-list-statistics';
import { RangeExt } from './range-ext';
import { VirtualListEdgeExt } from './virtual-list-edge-ext';
import { VirtualListAccessor } from './virtual-list-accessor';

export class VirtualListRenderPlan<TItem extends VirtualListItem>
{
    public RenderIndex: number = 0;
    public Viewport?: Range<number> = null;
    public ItemRange?: Range<number> = null;
    public UseSmoothScroll: boolean = false;
    public VirtualList?: VirtualListAccessor<TItem> = null;
    public ClientSideState?: VirtualListClientSideState = null;
    public ItemByKey: Record<string,  ItemRenderPlan> = null;
    public Items: ItemRenderPlan[] = null;
    public IsDataChanged: boolean = false;

    constructor(virtualList: VirtualListAccessor<TItem>) {
        this.VirtualList = virtualList;
        this.RenderIndex = 1;
        this.IsDataChanged = true;
        this.Update(null);
    }

    public get FullRange(): Range<number> | null {
        return this.ItemRange
               ? new Range(-this.SpacerSize,  this.ItemRange.End + this.EndSpacerSize)
               : null;
    }

    public get TrimmedLoadZoneRange(): Range<number> | null {
        return this.Viewport ? this.GetTrimmedLoadZoneRange(this.Viewport) : null
    }

    public get HasUnmeasuredItems(): boolean {
        return !this.ItemRange;
    }

    public get FirstItem(): ItemRenderPlan | null {
        return  this.Items.length > 0 ? this.Items[0] : null;
    }

    public get LastItem(): ItemRenderPlan | null {
        return this.Items.length > 0 ? this.Items[this.Items.length - 1] : null;
    }

    public get AlignmentEdge(): VirtualListEdge {
        return this.VirtualList.AlignmentEdge;
    }

    public get SpacerSize(): number {
        return this.VirtualList.RenderState.spacerSize;
    }

    public get EndSpacerSize(): number {
        return this.VirtualList.RenderState.endSpacerSize;
    }

    public get StickyEdge(): VirtualListStickyEdgeState | null {
        return this.ClientSideState?.stickyEdge;
    }

    protected get Statistics(): VirtualListStatistics {
        return this.VirtualList.Statistics;
    }

    public get IsFullyLoaded(): boolean | null {
        if (!this.ItemRange || !this.TrimmedLoadZoneRange)
            return null;

        return (this.VirtualList.RenderState.hasVeryFirstItem && this.VirtualList.RenderState.hasVeryLastItem)
            || RangeExt.Contains(this.ItemRange, this.TrimmedLoadZoneRange);
    }

    public Next(): VirtualListRenderPlan<TItem> {
        const plan = this.Clone();
        plan.RenderIndex++;
        plan.ClientSideState = this.VirtualList.ClientSideState;
        plan.Update(this);
        return plan;
    }

    public GetTrimmedLoadZoneRange(viewport: Range<number>): Range<number> {
        return new Range(
            viewport.Start - (this.VirtualList.RenderState.hasVeryFirstItem ? 0 : this.VirtualList.LoadZoneSize),
            viewport.End + (this.VirtualList.RenderState.hasVeryLastItem ? 0 : this.VirtualList.LoadZoneSize)
        );
    }

    private Update(lastPlan?: VirtualListRenderPlan<TItem>): void {
        const statistics = this.Statistics;
        const clientSideItems = this.ClientSideState?.items;
        const prevItemByKey = lastPlan?.ItemByKey;

        this.IsDataChanged = true;
        this.ItemByKey = {};
        this.Items = [];
        let hasUnmeasuredItems: boolean = false;
        let itemRange = new Range(0, 0);

        for (const [key, item] of Object.entries(this.VirtualList.RenderState.items)) {
            const newItem = new ItemRenderPlan(key, item);
            if (clientSideItems != null && clientSideItems[key] != null) {
                const clientSideItem = clientSideItems[key];
                const size = clientSideItem.size;
                statistics.AddItem(size, item.countAs);
                newItem.Range = new Range(0, size);
            } else if (prevItemByKey != null && prevItemByKey[key] != null) {
                const oldItem = prevItemByKey[key];
                newItem.Range = oldItem.Range;
            }

            this.Items.push(newItem);
            this.ItemByKey[key] = newItem;
            if (newItem.IsMeasured) {
                itemRange = new Range(itemRange.End, itemRange.End + newItem.Size);
                newItem.Range = itemRange;
            } else {
                hasUnmeasuredItems = true;
            }
        }
        this.ItemRange = hasUnmeasuredItems ? null : new Range(0, itemRange.End);
        this.UpdateViewport();
        this.UpdateClientSideState();
    }

    private UpdateViewport(): void
    {
        const result = VirtualListRenderPlan.TryGetClientSideViewport(this.ClientSideState);
        if (!result.success) {
            if (this.Viewport == null) {
                return;
            }
            if (this.HasUnmeasuredItems) {
                this.Viewport = null;
                return;
            }
        } else {
            if (this.FullRange == null) {
                this.Viewport = null;
                return;
            }
            this.Viewport = RangeExt.ScrollInto(
                result.viewport,
                this.FullRange,
                VirtualListEdgeExt.IsEnd(this.AlignmentEdge));
        }
    }

    private UpdateClientSideState(): void {
        if (this.ClientSideState == null)
            return;

        const newClientSideState: VirtualListClientSideState = {
            renderIndex: this.ClientSideState.renderIndex,
            spacerSize: this.ClientSideState.spacerSize,
            endSpacerSize: this.ClientSideState.endSpacerSize,
            scrollHeight: this.ClientSideState.scrollHeight,
            scrollTop: this.ClientSideState.scrollTop,
            viewportHeight: this.ClientSideState.viewportHeight,
            stickyEdge: this.ClientSideState.stickyEdge,
            scrollAnchorKey: this.ClientSideState.scrollAnchorKey,
            items: this.ClientSideState.items,
            visibleKeys: this.ClientSideState.visibleKeys,
            isViewportChanged: this.ClientSideState.isViewportChanged,
            isStickyEdgeChanged: this.ClientSideState.isStickyEdgeChanged,
            isUserScrollDetected: this.ClientSideState.isUserScrollDetected,
        };
        newClientSideState.spacerSize = this.SpacerSize;
        newClientSideState.endSpacerSize = this.EndSpacerSize;
        newClientSideState.scrollHeight = this.FullRange != null && this.Viewport != null
            ? Math.max(RangeExt.Size(this.FullRange), RangeExt.Size(this.Viewport))
            : null;
        newClientSideState.scrollTop = this.Viewport?.Start;
        newClientSideState.viewportHeight = this.Viewport ? RangeExt.Size( this.Viewport) : null;
        this.ClientSideState = newClientSideState;
    }

    private static TryGetClientSideViewport(
        clientSideState?: VirtualListClientSideState): { success: boolean, viewport?: Range<number> } {
        if (clientSideState?.viewportHeight == null || clientSideState?.scrollTop == null)
            return { success: false };
        const viewport = new Range(
            clientSideState.scrollTop,
            clientSideState.scrollTop + clientSideState.viewportHeight);
        return { success: true, viewport: viewport };
    }

    private Clone(): VirtualListRenderPlan<TItem> {
        return JSON.parse(JSON.stringify(this));
    }
}
