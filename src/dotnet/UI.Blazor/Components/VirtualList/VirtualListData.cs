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
    // Read by FiniteList only. It models every position from one item size, so a block separator's
    // extra height has to be known for the items it has never rendered too - otherwise the spacers
    // are short by one separator each and the difference lands in the scroll position.
    public IReadOnlyList<int>? SeparatorIndexes { get; init; }
    public int? EstimatedCount { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public string? ScrollToKey { get; init; }

    public bool? ScrollToKeyInTheMiddle { get; init; }
    public object? NavigationState { get; init; }
    public object? ItemVisibilityState { get; init; }
    public object? Metadata { get; init; }
    public CpuTimestamp ComputedAt { get; init; } = CpuTimestamp.Now;

    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public TItem? FirstItem => field ??= GetFirst(Items);

    public TItem? LastItem => field ??= GetLast(Items);

    public bool IsSimilarTo(VirtualListData<TItem> other)
        // A separator moving changes every position after it even when the loaded items are the same
        => ReferenceEquals(this, other) ||
            (HasVeryFirstItem == other.HasVeryFirstItem
            && HasVeryLastItem == other.HasVeryLastItem
            && ScrollToKey == other.ScrollToKey
            && (ReferenceEquals(SeparatorIndexes, other.SeparatorIndexes)
                || (SeparatorIndexes?.SequenceEqual(other.SeparatorIndexes ?? []) ?? false))
            && Items.SequenceEqual(other.Items));

    // Private members

    private static int CalculateCount(TItem item)
    {
        if (item is IVirtualListGroup<TItem> group)
            return group.Items.Sum(CalculateCount);

        return 1;
    }

    private static TItem? GetFirst(IReadOnlyList<TItem> items)
    {
        // Must resolve to a leaf: a group's own Key is its first item's, so returning the group
        // itself collapses KeyRange onto the group's start - and skip-key items around a group
        // are exactly what makes the scan land on one (e.g. the live block's header/footer).
        if (items.Count == 0)
            return null;

        var firstItem = items.SkipWhile(i => i.ShouldSkipKey).FirstOrDefault();
        return firstItem is IVirtualListGroup<TItem> group ? GetFirst(group.Items) : firstItem;
    }

    private static TItem? GetLast(IReadOnlyList<TItem> items)
    {
        if (items.Count == 0)
            return null;

        var lastItem = items.Reverse().SkipWhile(i => i.ShouldSkipKey).FirstOrDefault();
        return lastItem is IVirtualListGroup<TItem> group ? GetLast(group.Items) : lastItem;
    }
}
