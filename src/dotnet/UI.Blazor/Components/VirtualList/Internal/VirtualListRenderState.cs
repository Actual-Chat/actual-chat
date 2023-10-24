namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public sealed class VirtualListRenderState
{
    public long RenderIndex { get; init; }
    public VirtualListDataQuery Query { get; init; } = VirtualListDataQuery.None;

    public Range<string> KeyRange { get; init; }
    public double SpacerSize { get; init; }
    public double EndSpacerSize { get; init; }
    public int? RequestedStartExpansion { get; init; }
    public int? RequestedEndExpansion { get; init; }
    public int StartExpansion { get; init; }
    public int EndExpansion { get; init; }

    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }

    public string? ScrollToKey { get; init; }
}
