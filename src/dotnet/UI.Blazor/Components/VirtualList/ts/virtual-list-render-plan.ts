import { ItemRenderPlan } from './item-render-plan';
import { Range } from './range';
import { RangeExt } from './range-ext';
import { VirtualListEdge } from './virtual-list-edge';
import { VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListEdgeExt } from './virtual-list-edge-ext';
import { VirtualListAccessor } from './virtual-list-accessor';

export class VirtualListRenderPlan {
    public Viewport?: Range<number> = null;
    public ItemRange?: Range<number> = null;
    public VirtualList: VirtualListAccessor;
    public ItemByKey: Record<string, ItemRenderPlan>;
    public Items: ItemRenderPlan[];

    constructor(virtualList: VirtualListAccessor, lastPlan?: VirtualListRenderPlan) {
        this.VirtualList = virtualList;
        this.ItemByKey = {};
        this.Items = [];
        this.Update(lastPlan);
    }

    public get FullRange(): Range<number> | null {
        return this.ItemRange
               ? new Range(-this.SpacerSize, this.ItemRange.End + this.EndSpacerSize)
               : null;
    }

    public get TrimmedLoadZoneRange(): Range<number> | null {
        return this.Viewport ? this.GetTrimmedLoadZoneRange(this.Viewport) : null;
    }

    public get HasUnmeasuredItems(): boolean {
        return !this.ItemRange;
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

    public get IsFullyLoaded(): boolean | null {
        if (!this.ItemRange || !this.TrimmedLoadZoneRange)
            return null;

        return (this.VirtualList.RenderState.hasVeryFirstItem && this.VirtualList.RenderState.hasVeryLastItem)
            || RangeExt.Contains(this.ItemRange, this.TrimmedLoadZoneRange);
    }

    public Next(): VirtualListRenderPlan {
        return new VirtualListRenderPlan(this.VirtualList, this);
    }

    public GetTrimmedLoadZoneRange(viewport: Range<number>): Range<number> {
        return new Range(
            viewport.Start - (this.VirtualList.RenderState.hasVeryFirstItem ? 0 : this.VirtualList.LoadZoneSize),
            viewport.End + (this.VirtualList.RenderState.hasVeryLastItem ? 0 : this.VirtualList.LoadZoneSize),
        );
    }

    private Update(lastPlan?: VirtualListRenderPlan): void {
        const statistics = this.VirtualList.Statistics;
        const clientSideItems = this.VirtualList.ClientSideState?.items;
        const prevItemByKey = lastPlan?.ItemByKey;

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
    }

    private UpdateViewport(): void {
        const viewport = VirtualListRenderPlan.getClientSideViewport(this.VirtualList.ClientSideState);
        if (!viewport) {
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
                viewport,
                this.FullRange,
                VirtualListEdgeExt.IsEnd(this.AlignmentEdge));
        }
    }

    private static getClientSideViewport(clientSideState?: VirtualListClientSideState): Range<number> | null {
        if (clientSideState?.viewportHeight == null || clientSideState?.scrollTop == null)
            return null;
        return new Range(
            clientSideState.scrollTop,
            clientSideState.scrollTop + clientSideState.viewportHeight);
    }
}
