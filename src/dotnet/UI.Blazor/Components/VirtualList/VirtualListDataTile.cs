namespace ActualChat.UI.Blazor.Components;

public record VirtualListDataTile<TItem>(IReadOnlyList<TItem> Items, object? Source = null)
    where TItem : IVirtualListItem
{
    /// <summary>
    /// Inclusive range []
    /// </summary>
    public Range<string> KeyRange
        => Items.Count > 0
            ? new Range<string>(Items[0].Key, Items[^1].Key)
            : default;
}
