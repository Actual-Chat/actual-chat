
namespace ActualChat.UI.Blazor.App.Components;

// One VirtualList row of the content tabs: a month/day header, a media row (up to 3 tiles),
// or a single file/link entry. Equality is by Key + Version so VirtualList can skip
// re-renders of rows whose content hasn't changed.
public sealed class ContentListItem : IVirtualListItem, IEquatable<ContentListItem>
{
    public required string Key { get; init; }
    public bool IsHeader { get; init; }
    public bool IsEmptyPlaceholder { get; init; }
    public long Version { get; init; }

    public string GroupTitle { get; init; } = "";
    public string VisorDate { get; init; } = "";
    public IReadOnlyList<IChatContentItem> Items { get; init; } = [];
    public ChatEntry? LinkEntry { get; init; }

    // Date headers are ordinary list items (so they occupy space in the scroll geometry) that merely skip
    // the key protocol: their synthetic "g:" key must never anchor a load query or a sticky edge. They are
    // not VirtualList groups — those wrap child items, headers don't.
    public bool IsGroup => false;
    public bool ShouldSkipKey => IsHeader;

    public bool Equals(ContentListItem? other)
        => other is not null && Key == other.Key && Version == other.Version;
    public override bool Equals(object? obj)
        => obj is ContentListItem other && Equals(other);
    public override int GetHashCode()
        => HashCode.Combine(Key, Version);
}
