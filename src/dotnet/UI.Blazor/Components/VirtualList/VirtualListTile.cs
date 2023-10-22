namespace ActualChat.UI.Blazor.Components;

public sealed record VirtualListTile<TItem>(IReadOnlyList<TItem> Items)
    where TItem : IVirtualListItem
{
    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(Items[0].Key, Items[^1].Key)
            : default;

    // This record relies on referential equality
    public bool Equals(VirtualListTile<TItem>? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
