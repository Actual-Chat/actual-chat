import { Range} from './range';
import { RangeExt } from './range-ext';
import { VirtualListRenderItem } from './virtual-list-render-state';

export class ItemRenderPlan {
    constructor(public Key: string, public Item: VirtualListRenderItem) {
    }

    Range: Range<number> = new Range(-1, -2);

    get Size(): number {
        return RangeExt.Size(this.Range);
    }

    get IsMeasured(): boolean {
        return this.Size >= 0;
    }
}
