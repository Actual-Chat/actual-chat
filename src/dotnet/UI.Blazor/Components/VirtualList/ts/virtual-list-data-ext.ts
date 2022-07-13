import { VirtualListItem } from './virtual-list-item';
import { VirtualListData } from './virtual-list-data';
import { VirtualListDataQuery } from './virtual-list-data-query';

export class VirtualListDataExt
{
    public static New<TItem extends VirtualListItem>(
        query: VirtualListDataQuery,
        items: TItem[] ,
        hasVeryFirstItem: boolean = false,
        hasVeryLastItem: boolean = false,
        scrollToKey: string | null = null): VirtualListData<TItem>
    {
        const data = new VirtualListData(query, [...items]);
        data.HasVeryFirstItem = hasVeryFirstItem;
        data.HasVeryLastItem = hasVeryLastItem;
        data.ScrollToKey = scrollToKey;

        return data;
    }
}
