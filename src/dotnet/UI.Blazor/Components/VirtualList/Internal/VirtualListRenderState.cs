namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public sealed class VirtualListRenderState
{
    public long RenderIndex { get; init; }
    public VirtualListDataQuery Query { get; init; } = VirtualListDataQuery.None;

    public Range<string> KeyRange { get; init; }
    public int? BeforeCount { get; init; }
    public int? AfterCount { get; init; }
    public int Count { get; init; }

    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }

    public string? ScrollToKey { get; init; }
}
