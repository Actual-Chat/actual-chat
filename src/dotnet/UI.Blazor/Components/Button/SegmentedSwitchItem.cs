namespace ActualChat.UI.Blazor.Components;

public sealed record SegmentedSwitchItem(string Id, string Title)
{
    public bool HasDot { get; init; }
}
