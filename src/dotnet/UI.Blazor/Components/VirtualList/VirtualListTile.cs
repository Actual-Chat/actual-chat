namespace ActualChat.UI.Blazor.Components;

public sealed record VirtualListTile<TItem>(string Key, IReadOnlyList<TItem> Items)
    where TItem : IVirtualListItem
{
    public static VirtualListTile<TItem> Empty = new("tile:empty", Array.Empty<TItem>());

    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(Items[0].Key, Items[^1].Key)
            : default;

    public VirtualListTile(Range<long> idRange, IReadOnlyList<TItem>? Items = null)
        : this($"tile:{idRange.Format()}", Items ?? Array.Empty<TItem>()) { }

    // This record relies on referential equality
    public bool Equals(VirtualListTile<TItem>? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
