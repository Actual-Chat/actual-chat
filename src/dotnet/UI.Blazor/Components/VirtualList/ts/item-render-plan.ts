import { VirtualListItem } from './virtual-list-item';
import { Range} from './range';
import { RangeExt } from './range-ext';

export class ItemRenderPlan<TItem extends VirtualListItem> {
    constructor(public Item: TItem) {
    }

    Range: Range<number> = new Range(-1, -2);

    get Key(): string {
        return this.Item.Key;
    }

    get Size(): number {
        return RangeExt.Size(this.Range);
    }

    get IsMeasured(): boolean {
        return this.Size >= 0;
    }
}
