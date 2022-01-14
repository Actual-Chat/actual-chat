namespace ActualChat.UI.Blazor.Controls;

public record VirtualListData<TItem>(List<VirtualListItem<TItem>> Items)
{
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;

    public VirtualListData() : this(new List<VirtualListItem<TItem>>()) { }
}

public static class VirtualListData
{
    public static VirtualListData<TItem> New<TItem>(
        List<VirtualListItem<TItem>> items,
        bool hasVeryFirstItem = false,
        bool hasVeryLastItem = false)
        => new(items) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };

    public static VirtualListData<TItem> New<TItem>(
        IEnumerable<VirtualListItem<TItem>> items,
        bool hasVeryFirstItem = false,
        bool hasVeryLastItem = false)
        => new(items.ToList()) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };

    public static VirtualListData<TItem> New<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, string> keySelector,
        bool hasVeryFirstItem = false,
        bool hasVeryLastItem = false)
        => new(items.Select(item => new VirtualListItem<TItem>(keySelector.Invoke(item), item)).ToList()) {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };
}
