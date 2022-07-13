import { VirtualListEdge } from './virtual-list-edge';

export class VirtualListEdgeExt {
    public static IsStart(edge: VirtualListEdge): boolean {
        return edge == VirtualListEdge.Start;
    }

    public static IsEnd(edge: VirtualListEdge): boolean {
        return edge == VirtualListEdge.End;
    }
}
