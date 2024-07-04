import { NumberRange, Range } from './range';

export class VirtualListDataQuery
{
    public static None: VirtualListDataQuery = new VirtualListDataQuery(
        new Range<string>(null, null),
        new NumberRange(0, 0),
        new NumberRange(0, 0));

    public expectedCount?: number = null;

    constructor(public keyRange: Range<string>, public virtualRange: NumberRange, public moveRange: NumberRange)
    { }

    public get isNone(): boolean {
        return this === VirtualListDataQuery.None;
    }
}
