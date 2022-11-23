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
}
