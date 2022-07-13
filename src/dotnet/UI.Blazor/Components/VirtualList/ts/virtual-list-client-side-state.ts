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

    itemSizes: Record<string, number>;
    visibleKeys: string[];

    isViewportChanged: boolean;
    isStickyEdgeChanged: boolean;
    isUserScrollDetected: boolean;
}
