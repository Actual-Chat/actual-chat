namespace ActualChat.UI.Blazor.Controls;

public class VirtualListData<TItem>
    where TItem : IVirtualListItem
{
#pragma warning disable MA0018
    public static VirtualListData<TItem> None { get; } = new(VirtualListDataQuery.None, Array.Empty<TItem>());
#pragma warning restore MA0018

    public VirtualListDataQuery Query { get; }
    public IReadOnlyCollection<TItem> Items { get; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public Symbol ScrollToKey { get; init; }

    public VirtualListData(VirtualListDataQuery query, IReadOnlyCollection<TItem> items)
    {
        Query = query;
        Items = items;
    }
}

public static class VirtualListData
{
    public static VirtualListData<TItem> New<TItem>(
        VirtualListDataQuery query,
        IEnumerable<TItem> items,
        bool hasVeryFirstItem = false,
        bool hasVeryLastItem = false,
        Symbol scrollToKey = default)
        where TItem : IVirtualListItem
    {
        var readOnlyItems = items as IReadOnlyCollection<TItem> ?? items.ToList();
        return new(query, readOnlyItems) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = scrollToKey,
        };
    }
}
