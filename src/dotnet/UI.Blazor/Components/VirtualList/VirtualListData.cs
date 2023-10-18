namespace ActualChat.UI.Blazor.Components;

public sealed class VirtualListData<TItem>(IReadOnlyList<VirtualListDataTile<TItem>> tiles)
    where TItem : IVirtualListItem
{
    public static readonly VirtualListData<TItem> None = new(Array.Empty<VirtualListDataTile<TItem>>());

    public bool IsNone
        => ReferenceEquals(this, None);

    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Tiles.Count > 0
            ? new Range<string>(Tiles[0].KeyRange.Start, Tiles[^1].KeyRange.End)
            : default;

    public IReadOnlyList<VirtualListDataTile<TItem>> Tiles { get; } = tiles;
    public int? RequestedStartExpansion { get; init; }
    public int? RequestedEndExpansion { get; init; }
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }
    public bool HasAllItems => HasVeryFirstItem && HasVeryLastItem;
    public string? ScrollToKey { get; init; }
}
