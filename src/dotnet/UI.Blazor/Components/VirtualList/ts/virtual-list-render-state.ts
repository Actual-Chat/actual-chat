import { VirtualListDataQuery } from './virtual-list-data-query';
import { Range } from './range';

export interface VirtualListRenderState {
    renderIndex: number;

    query: VirtualListDataQuery;
    keyRange: Range<string>;
    beforeCount: number | null;
    afterCount: number | null;
    // Indexes of items followed by a block separator, across the whole list. FiniteList only.
    separatorIndexes?: number[] | null;
    count: number;
    estimatedCount: number | null;
    hasVeryFirstItem: boolean;
    hasVeryLastItem: boolean;

    scrollToKey?: string;
    scrollToKeyInTheMiddle?: boolean;
}

