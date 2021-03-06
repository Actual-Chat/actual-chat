import { VirtualListDataQuery } from './virtual-list-data-query';

export interface VirtualListRenderState {
    renderIndex: number;

    query: VirtualListDataQuery;
    spacerSize: number;
    endSpacerSize: number;
    startExpansion: number;
    endExpansion: number;
    hasVeryFirstItem: boolean;
    hasVeryLastItem: boolean;

    scrollToKey?: string;
    useSmoothScroll: boolean;
    items: Record<string, VirtualListRenderItem>;
}

export interface VirtualListRenderItem {
    countAs: number;
    dataHash: number;
}
