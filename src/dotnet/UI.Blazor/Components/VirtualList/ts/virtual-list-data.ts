import { VirtualListItem } from './virtual-list-item';
import { VirtualListDataQuery } from './virtual-list-data-query';

export class VirtualListData<TItem extends VirtualListItem>
{
    public static None: VirtualListData<any> = new VirtualListData(VirtualListDataQuery.None, []);

    public readonly Query: VirtualListDataQuery;
    public readonly Items: TItem[];

    public HasVeryFirstItem: boolean = false;
    public HasVeryLastItem: boolean = false
    public ScrollToKey?: string = null;

    constructor(query: VirtualListDataQuery, items: TItem[])
    {
        this.Query = query;
        this.Items = items;
    }

    public get HasAllItems(): boolean {
        return this.HasVeryFirstItem && this.HasVeryLastItem;
    }
}
