namespace ActualChat.UI.Blazor.Controls;

public record VirtualListData<TItem>(IReadOnlyCollection<TItem> Items)
    where TItem : IVirtualListItem
{
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;

    public VirtualListData() : this(Array.Empty<TItem>()) { }
}

public static class VirtualListData
{
    public static VirtualListData<TItem> New<TItem>(
        IEnumerable<TItem> items,
        bool hasVeryFirstItem = false,
        bool hasVeryLastItem = false)
        where TItem : IVirtualListItem
    {
        var readOnlyCollection = items as IReadOnlyCollection<TItem> ?? items.ToList();
        return new(readOnlyCollection) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };
    }
}
