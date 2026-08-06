namespace ActualChat.UI.Blazor.Components;

public sealed record VirtualListItemVisibility(
    string ListIdentity,
    IReadOnlySet<string> VisibleKeys,
    bool IsEndAnchorVisible,
    bool IsPinnedToEnd
)
{
    public static readonly VirtualListItemVisibility Empty = new("", ImmutableHashSet<string>.Empty, true, true);
    public bool IsEmpty => VisibleKeys.Count == 0;
}
