import { VirtualListEdge } from './virtual-list-edge';

export class VirtualListEdgeExt {
    public static isStart(edge: VirtualListEdge): boolean {
        return edge == VirtualListEdge.Start;
    }

    public static isEnd(edge: VirtualListEdge): boolean {
        return edge == VirtualListEdge.End;
    }
}
