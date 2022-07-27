import { Range } from './range';
import { RangeExt } from './range-ext';
import { VirtualListClientSideItem } from './virtual-list-client-side-state';

export class ItemRenderPlan {
    constructor(public Key: string, public Item: VirtualListClientSideItem) {
    }

    range: Range<number> = new Range(-1, -2);

    get size(): number {
        return RangeExt.size(this.range);
    }

    get isMeasured(): boolean {
        return this.size >= 0;
    }
}
