namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<VirtualListTile<TItem>> tiles)
    where TItem : class, IVirtualListItem
{
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
    public int? RequestedStartExpansion { get; init; }
    public int? RequestedEndExpansion { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public string? ScrollToKey { get; init; }

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
}
