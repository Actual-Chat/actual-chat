import { VirtualListStickyEdgeState } from './virtual-list-sticky-edge-state';

export interface VirtualListRenderState {
    renderIndex: number;

    spacerSize: number;
    endSpacerSize: number;
    scrollHeight?: number;
    scrollTop?: number;
    viewportHeight?: number;
    hasVeryFirstItem?: boolean;
    hasVeryLastItem?: boolean;

    scrollToKey?: string;
    useSmoothScroll: boolean;

    itemSizes: Record<string, number>;
    hasUnmeasuredItems: boolean;
    stickyEdge?: Required<VirtualListStickyEdgeState>;
}
