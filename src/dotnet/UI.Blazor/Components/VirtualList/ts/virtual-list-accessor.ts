import { VirtualListStatistics } from './virtual-list-statistics';
import { VirtualListClientSideItem, VirtualListClientSideState } from './virtual-list-client-side-state';
import { VirtualListRenderState } from './virtual-list-render-state';

export interface VirtualListAccessor {
    loadZoneSize: number;
    statistics: VirtualListStatistics;
    renderState: VirtualListRenderState;
    clientSideState: VirtualListClientSideState;

    items: Record<string, VirtualListClientSideItem>;
}
