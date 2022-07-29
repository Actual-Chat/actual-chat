import { VirtualListStickyEdgeState } from './virtual-list-sticky-edge-state';

export interface VirtualListClientSideState {
    renderIndex: number;

    spacerSize: number;
    endSpacerSize: number;
    scrollHeight: number;
    scrollTop: number;
    viewportHeight: number;
    stickyEdge?: Required<VirtualListStickyEdgeState>;
    scrollAnchorKey?: string,

    items: Record<string, VirtualListClientSideItem>;
    visibleKeys: string[];
}

export interface VirtualListClientSideItem {
    size: number;
    countAs: number;
}

