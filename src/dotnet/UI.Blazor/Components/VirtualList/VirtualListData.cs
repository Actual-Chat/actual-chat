namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<TItem> items)
    where TItem : class, IVirtualListItem
{
    public static readonly VirtualListData<TItem> None = new(Array.Empty<TItem>());

    public bool IsNone
        => ReferenceEquals(this, None);

    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(Items[0].Key, Items[^1].Key)
            : default;

    public IReadOnlyList<TItem> Items { get; } = items;
    public int Index { get; init; }
    public int? BeforeCount { get; init; }
    public int? AfterCount { get; init; }
    public int? EstimatedCount { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public string? ScrollToKey { get; init; }
    public object? NavigationState { get; init; }
    public object? ItemVisibilityState { get; init; }
    public CpuTimestamp ComputedAt { get; init; } = CpuTimestamp.Now;

    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public TItem? FirstItem => Items.Count == 0 ? null : Items[0];
    public TItem? LastItem => Items.Count == 0 ? null : Items[^1];

    public bool IsSimilarTo(VirtualListData<TItem> other)
        => HasVeryFirstItem == other.HasVeryFirstItem
            && HasVeryLastItem == other.HasVeryLastItem
            && OrdinalEquals(ScrollToKey, other.ScrollToKey)
            && Items.SequenceEqual(other.Items);
}
