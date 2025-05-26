namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<TItem> items)
    where TItem : class, IVirtualListItem
{
    public static readonly VirtualListData<TItem> None = new([]);
    private int? _count;

    public bool IsNone
        => ReferenceEquals(this, None);

    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(FirstItem!.Key, LastItem!.Key)
            : default;

    public IReadOnlyList<TItem> Items { get; } = items;
    public int Index { get; init; }
    public int Count => _count ??= Items.Sum(CalculateCount);

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
    public TItem? FirstItem => Items.Count > 0
        ? Items[0] is IVirtualListGroup<TItem> { Count: > 0 } firstGroup
            ? firstGroup.Items[0]
            : Items[0]
        : null;

    public TItem? LastItem => Items.Count > 0
        ? Items[^1] is IVirtualListGroup<TItem> { Count: > 0 } lastGroup
            ? lastGroup.Items[^1]
            : Items[^1]
        : null;

    public bool IsSimilarTo(VirtualListData<TItem> other)
        => ReferenceEquals(this, other) ||
            (HasVeryFirstItem == other.HasVeryFirstItem
            && HasVeryLastItem == other.HasVeryLastItem
            && OrdinalEquals(ScrollToKey, other.ScrollToKey)
            && Items.SequenceEqual(other.Items));


    // Private members

    private static int CalculateCount(TItem item)
    {
        if (item is IVirtualListGroup<TItem> group)
            return group.Items.Sum(CalculateCount);

        return 1;
    }
}
