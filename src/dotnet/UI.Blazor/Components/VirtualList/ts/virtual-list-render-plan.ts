import { ItemRenderPlan } from './item-render-plan';
import { Range } from './range';
import { RangeExt } from './range-ext';
import { VirtualListEdge } from './virtual-list-edge';
import { VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListEdgeExt } from './virtual-list-edge-ext';
import { VirtualListAccessor } from './virtual-list-accessor';

export class VirtualListRenderPlan {
    public viewport?: Range<number> = null;
    public itemRange?: Range<number> = null;
    public virtualList: VirtualListAccessor;
    public itemByKey: Record<string, ItemRenderPlan>;
    public items: ItemRenderPlan[];

    constructor(virtualList: VirtualListAccessor, lastPlan?: VirtualListRenderPlan) {
        this.virtualList = virtualList;
        this.itemByKey = {};
        this.items = [];
        this.update(lastPlan);
    }

    public get fullRange(): Range<number> | null {
        return this.itemRange
               ? new Range(-this.spacerSize, this.itemRange.End + this.endSpacerSize)
               : null;
    }

    public get trimmedLoadZoneRange(): Range<number> | null {
        return this.viewport ? this.getTrimmedLoadZoneRange(this.viewport) : null;
    }

    public get hasUnmeasuredItems(): boolean {
        return !this.itemRange;
    }

    public get spacerSize(): number {
        return this.virtualList.renderState.spacerSize;
    }

    public get endSpacerSize(): number {
        return this.virtualList.renderState.endSpacerSize;
    }

    public get isFullyLoaded(): boolean | null {
        if (!this.itemRange || !this.trimmedLoadZoneRange)
            return null;

        return (this.virtualList.renderState.hasVeryFirstItem && this.virtualList.renderState.hasVeryLastItem)
            || RangeExt.contains(this.itemRange, this.trimmedLoadZoneRange);
    }

    public next(): VirtualListRenderPlan {
        return new VirtualListRenderPlan(this.virtualList, this);
    }

    public getTrimmedLoadZoneRange(viewport: Range<number>): Range<number> {
        return new Range(
            viewport.Start - (this.virtualList.renderState.hasVeryFirstItem ? 0 : this.virtualList.loadZoneSize),
            viewport.End + (this.virtualList.renderState.hasVeryLastItem ? 0 : this.virtualList.loadZoneSize),
        );
    }

    private update(lastPlan?: VirtualListRenderPlan): void {
        const statistics = this.virtualList.statistics;
        const clientSideItems = this.virtualList.clientSideState.items;
        const prevItemByKey = lastPlan?.itemByKey;

        let hasUnmeasuredItems: boolean = false;
        let itemRange = new Range(0, 0);

        for (const [key, item] of Object.entries(clientSideItems)) {
            const newItem = new ItemRenderPlan(key, item);
            statistics.addItem(item.size, item.countAs);
            newItem.range = new Range(0, item.size);

            this.items.push(newItem);
            this.itemByKey[key] = newItem;
            if (newItem.isMeasured) {
                itemRange = new Range(itemRange.End, itemRange.End + newItem.size);
                newItem.range = itemRange;
            } else {
                hasUnmeasuredItems = true;
            }
        }
        this.itemRange = hasUnmeasuredItems ? null : new Range(0, itemRange.End);
        this.updateViewport();
    }

    private updateViewport(): void {
        const viewport = VirtualListRenderPlan.getClientSideViewport(this.virtualList.clientSideState);
        if (!viewport) {
            console.warn('viewport is null');
            if (this.viewport == null) {
                return;
            }
            if (this.hasUnmeasuredItems) {
                this.viewport = null;
                return;
            }
        } else {
            if (this.fullRange == null) {
                this.viewport = null;
                return;
            }
            this.viewport = RangeExt.scrollInto(
                viewport,
                this.fullRange,
                true);
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
