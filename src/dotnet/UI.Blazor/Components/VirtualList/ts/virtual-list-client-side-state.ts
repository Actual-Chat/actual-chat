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

    isViewportChanged: boolean;
    isStickyEdgeChanged: boolean;
    isUserScrollDetected: boolean;
}

export interface VirtualListClientSideItem {
    size: number;
    dataHash: number;
}

