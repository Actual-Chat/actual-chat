import { VirtualListDataQuery } from "./virtual-list-data-query";
import {Range} from "./range";

export interface VirtualListRenderState {
    renderIndex: number;

    query: VirtualListDataQuery;
    keyRange: Range<string>;
    beforeCount: number | null;
    afterCount: number | null;
    count: number;
    estimatedCount: number | null;
    hasVeryFirstItem: boolean;
    hasVeryLastItem: boolean;

    scrollToKey?: string;
    scrollToKeyInTheMiddle?: boolean;
}

