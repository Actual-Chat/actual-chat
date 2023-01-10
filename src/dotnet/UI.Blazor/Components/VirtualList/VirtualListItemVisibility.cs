namespace ActualChat.UI.Blazor.Components;

public sealed record VirtualListItemVisibility(
    IReadOnlySet<string> VisibleKeys,
    bool IsEndAnchorVisible
)
{
    public static VirtualListItemVisibility Empty { get; } = new(ImmutableHashSet<string>.Empty, true);

    public bool IsEmpty => VisibleKeys.Count == 0;
}
