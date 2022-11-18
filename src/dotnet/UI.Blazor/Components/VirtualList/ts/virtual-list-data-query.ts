import { NumberRange, Range } from './range';

export class VirtualListDataQuery
{
    public static None: VirtualListDataQuery = new VirtualListDataQuery();

    public expandStartBy: number = 0;
    public expandEndBy: number = 0;

    constructor(public keyRange?: Range<string>, public virtualRange?: NumberRange)
    { }

    public get isNone(): boolean {
        return this === VirtualListDataQuery.None;
    }

    public isSimilarTo(other: VirtualListDataQuery, viewport?: NumberRange): boolean
    {
        if (this === other)
            return true;

        if (!this.virtualRange || !other.virtualRange)
            return false;

        if (!viewport)
            return false;

        const viewportSize = viewport.size;
        const intersection = this.virtualRange.intersectWith(other.virtualRange);
        if (intersection.isEmpty)
            return false;

        return !(Math.abs(this.virtualRange.Start - intersection.Start) > viewportSize / 2)
            && !(Math.abs(this.virtualRange.Start - intersection.End) > viewportSize / 2);
    }
}
