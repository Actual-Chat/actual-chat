import { VirtualListItem } from './virtual-list-item';
import { VirtualListStatistics } from './virtual-list-statistics';
import { VirtualListEdge } from './virtual-list-edge';
import { VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListData } from './virtual-list-data';
import { VirtualListRenderState } from './virtual-list-render-state';

export interface VirtualListAccessor<TItem extends VirtualListItem> {
    SpacerSize: number;
    LoadZoneSize: number;
    Statistics: VirtualListStatistics;
    AlignmentEdge: VirtualListEdge;
    RenderState: VirtualListRenderState;
    ClientSideState: VirtualListClientSideState;
}
