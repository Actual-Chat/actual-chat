namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<TItem> items)
    where TItem : IVirtualListItem
{
    public static VirtualListData<TItem> None { get; } = new(Array.Empty<TItem>());

    public bool IsNone
        => ReferenceEquals(this, None);

    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(Items[0].Key, Items[^1].Key)
            : default;

    public IReadOnlyList<TItem> Items { get; } = items;
    public int? RequestedStartExpansion { get; init; }
    public int? RequestedEndExpansion { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public string? ScrollToKey { get; init; }
}
