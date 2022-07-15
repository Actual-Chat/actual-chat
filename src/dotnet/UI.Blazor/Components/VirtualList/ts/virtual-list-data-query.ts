import { Range} from './range';

export class VirtualListDataQuery
{
    public static None: VirtualListDataQuery = new VirtualListDataQuery();

    public InclusiveRange?: Range<string> = null;
    public ScrollToKey?: string = null;
    public ExpandStartBy: number = 0;
    public ExpandEndBy: number = 0;

    constructor(inclusiveRange?: Range<string>) {
         this.InclusiveRange = inclusiveRange;
    }

    public get IsNone(): boolean {
        return this === VirtualListDataQuery.None;
    }

    public IsSimilarTo(other: VirtualListDataQuery): boolean
    {
        if (this === other)
            return true;

        if (!this.InclusiveRange.Equals(other.InclusiveRange))
            return false;

        const epsilon: number = 10;
        return !(Math.abs(this.ExpandStartBy - other.ExpandStartBy) > epsilon)
            && !(Math.abs(this.ExpandEndBy - other.ExpandEndBy) > epsilon);
    }
}
