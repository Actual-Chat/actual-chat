import { Range} from './range';

export class VirtualListDataQuery
{
    public static None: VirtualListDataQuery = new VirtualListDataQuery();

    public inclusiveRange?: Range<string> = null;
    public expandStartBy: number = 0;
    public expandEndBy: number = 0;

    constructor(inclusiveRange?: Range<string>) {
         this.inclusiveRange = inclusiveRange;
    }

    public get isNone(): boolean {
        return this === VirtualListDataQuery.None;
    }

    public isSimilarTo(other: VirtualListDataQuery): boolean
    {
        if (this === other)
            return true;

        const epsilon: number = 10;
        return !(Math.abs(this.expandStartBy - other.expandStartBy) > epsilon)
            && !(Math.abs(this.expandEndBy - other.expandEndBy) > epsilon);
    }
}
