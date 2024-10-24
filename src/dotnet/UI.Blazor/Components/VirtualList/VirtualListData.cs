namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<VirtualListTile<TItem>> tiles)
    where TItem : class, IVirtualListItem
{
    private static readonly EqualityComparer<VirtualListTile<TItem>> TileComparer = EqualityComparer<VirtualListTile<TItem>>
        .Create(Equals);

    public static readonly VirtualListData<TItem> None = new(Array.Empty<VirtualListTile<TItem>>());

    public bool IsNone
        => ReferenceEquals(this, None);

    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Tiles.Count > 0
            ? new Range<string>(Tiles[0].KeyRange.Start, Tiles[^1].KeyRange.End)
            : default;

    public IReadOnlyList<VirtualListTile<TItem>> Tiles { get; } = tiles;
    public int Index { get; init; }
    public int? BeforeCount { get; init; }
    public int? AfterCount { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public string? ScrollToKey { get; init; }
    public object? NavigationState { get; init; }
    public object? ItemVisibilityState { get; init; }
    public CpuTimestamp ComputedAt { get; init; } = CpuTimestamp.Now;

    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public TItem? FirstItem => Tiles.Count == 0 ? null : Tiles[0].Items[0];
    public TItem? LastItem => Tiles.Count == 0 ? null : Tiles[^1].Items[^1];

    public TItem? GetNthItem(int n, bool reverse = false)
        => GetItems(reverse).Skip(n).FirstOrDefault();

    public IEnumerable<TItem> GetItems(bool reverse = false)
    {
        return reverse
            ? GetItemsReversed(Tiles)
            : Tiles.SelectMany(x => x.Items);

        static IEnumerable<TItem> GetItemsReversed(IReadOnlyList<VirtualListTile<TItem>> tiles) {
            for (var i = tiles.Count - 1; i >= 0; i--) {
                var items = tiles[i].Items;
                for (var j = items.Count - 1; j >= 0; j--)
                    yield return items[j];
            }
        }
    }

    public bool IsSimilarTo(VirtualListData<TItem> other)
        => HasVeryFirstItem == other.HasVeryFirstItem
            && HasVeryLastItem == other.HasVeryLastItem
            && OrdinalEquals(ScrollToKey, other.ScrollToKey)
            && Tiles.SequenceEqual(other.Tiles, TileComparer);

    private static bool Equals(VirtualListTile<TItem>? x, VirtualListTile<TItem>? y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null)
            return false;

        return OrdinalEquals(x.Key, y.Key)
            && x.Items.Count == y.Items.Count
            && x.Items.SequenceEqual(y.Items);
    }
}
