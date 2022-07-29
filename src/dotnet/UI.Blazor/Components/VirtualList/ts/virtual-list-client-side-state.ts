import { VirtualListStickyEdgeState } from './virtual-list-sticky-edge-state';

export interface VirtualListClientSideState {
    renderIndex: number;

    scrollTop: number;
    viewportHeight: number;
    stickyEdge: Required<VirtualListStickyEdgeState> | null;

    visibleKeys: string[];
}

export interface VirtualListClientSideItem {
    size: number;
    countAs: number;
}

