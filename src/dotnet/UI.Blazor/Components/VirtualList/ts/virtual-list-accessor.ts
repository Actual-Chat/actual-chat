import { VirtualListStatistics } from './virtual-list-statistics';
import { VirtualListEdge } from './virtual-list-edge';
import { VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListRenderState } from './virtual-list-render-state';

export interface VirtualListAccessor {
    LoadZoneSize: number;
    Statistics: VirtualListStatistics;
    AlignmentEdge: VirtualListEdge;
    RenderState: VirtualListRenderState;
    ClientSideState: VirtualListClientSideState;
}
