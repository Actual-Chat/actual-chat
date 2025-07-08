import { NumberRange, Range } from './range';

export class VirtualListDataQuery
{
    public static None: VirtualListDataQuery = new VirtualListDataQuery(
        new Range<string>('', ''),
        new NumberRange(0, 0),
        new NumberRange(0, 0));

    public expectedCount?: number;

    constructor(public keyRange: Range<string>, public virtualRange: NumberRange, public moveRange: NumberRange)
    { }

    public get isNone(): boolean {
        return this === VirtualListDataQuery.None;
    }
}
