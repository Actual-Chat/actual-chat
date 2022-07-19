import { Range } from './range';
import { RangeExt } from './range-ext';
import { VirtualListRenderItem } from './virtual-list-render-state';

export class ItemRenderPlan {
    constructor(public Key: string, public Item: VirtualListRenderItem) {
    }

    range: Range<number> = new Range(-1, -2);

    get size(): number {
        return RangeExt.size(this.range);
    }

    get isMeasured(): boolean {
        return this.size >= 0;
    }
}
