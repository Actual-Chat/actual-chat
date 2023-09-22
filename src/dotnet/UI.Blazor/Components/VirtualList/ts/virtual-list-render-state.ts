import { VirtualListDataQuery } from "./virtual-list-data-query";
import {Range} from "./range";

export interface VirtualListRenderState {
    renderIndex: number;

    query: VirtualListDataQuery;
    keyRange: Range<string>;
    spacerSize: number;
    endSpacerSize: number;
    requestedStartExpansion?: number;
    requestedEndExpansion?: number;
    startExpansion: number;
    endExpansion: number;
    hasVeryFirstItem: boolean;
    hasVeryLastItem: boolean;

    scrollToKey?: string;
}

