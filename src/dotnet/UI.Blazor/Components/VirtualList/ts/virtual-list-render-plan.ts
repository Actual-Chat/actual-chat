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
    public Data?: VirtualListData<TItem> = null;
    public ItemByKey: Record<string,  ItemRenderPlan<TItem>> = null;
    public Items: ItemRenderPlan<TItem>[] = null;
    public IsDataChanged: boolean = false;

    constructor(virtualList: VirtualListAccessor<TItem>) {
        this.VirtualList = virtualList;
        this.RenderIndex = 1;
        this.Data = VirtualListData.None;
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

    public get ReversedItems(): ItemRenderPlan<TItem>[] {
        return this.Items.reverse();
    }

    public get FirstItem(): ItemRenderPlan<TItem> | null {
        return  this.Items.length > 0 ? this.Items[0] : null;
    }

    public get LastItem(): ItemRenderPlan<TItem> | null {
        return this.Items.length > 0 ? this.Items[this.Items.length - 1] : null;
    }

    public get AlignmentEdge(): VirtualListEdge {
        return this.VirtualList.AlignmentEdge;
    }

    public get SpacerSize(): number {
        return this.Data.HasVeryFirstItem ? 0 : this.VirtualList.SpacerSize;
    }

    public get EndSpacerSize(): number {
        return this.Data.HasVeryLastItem ? 0 : this.VirtualList.SpacerSize
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

        return this.Data.HasAllItems || RangeExt.Contains(this.ItemRange, this.TrimmedLoadZoneRange);
    }

    public Next(): VirtualListRenderPlan<TItem> {
        const plan = this.Clone();
        plan.RenderIndex++;
        plan.Data = this.VirtualList.Data;
        plan.ClientSideState = this.VirtualList.ClientSideState;
        plan.Update(this);
        return plan;
    }

    public GetTrimmedLoadZoneRange(viewport: Range<number>): Range<number> {
        return new Range(
            viewport.Start - (this.Data.HasVeryFirstItem ? 0 : this.VirtualList.LoadZoneSize),
            viewport.End + (this.Data.HasVeryLastItem ? 0 : this.VirtualList.LoadZoneSize)
        );
    }

    private Update(lastPlan?: VirtualListRenderPlan<TItem>): void {
        const statistics = this.Statistics;
        const newItemSizes = this.ClientSideState?.itemSizes;
        const prevItemByKey = lastPlan?.ItemByKey;

        this.IsDataChanged = lastPlan?.Data != this.Data;
        this.ItemByKey = {};
        this.Items = [];
        let hasUnmeasuredItems: boolean = false;
        let itemRange = new Range(0, 0);

        for (const item of this.Data.Items) {
            const newItem = new ItemRenderPlan(item);
            if (newItemSizes != null && newItemSizes[item.Key] != null) {
                const newSize = newItemSizes[item.Key];
                statistics.AddItem(newSize, item.CountAs);
                newItem.Range = new Range(0, newSize);
            } else if (prevItemByKey != null && prevItemByKey[item.Key] != null) {
                const oldItem = prevItemByKey[item.Key];
                newItem.Range = oldItem.Range;
            }

            this.Items.push(newItem);
            this.ItemByKey[item.Key] = newItem;
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
        const result = this.TryGetClientSideViewport(this.ClientSideState);
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
            itemSizes: this.ClientSideState.itemSizes,
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

    private TryGetClientSideViewport(
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
