namespace ActualChat.UI.Blazor.Components;

public sealed record VirtualListItemVisibility(
    string ListIdentity,
    IReadOnlySet<string> VisibleKeys,
    bool IsEndAnchorVisible
)
{
    public static readonly VirtualListItemVisibility Empty = new("", ImmutableHashSet<string>.Empty, true);

    public bool IsEmpty => VisibleKeys.Count == 0;
}
