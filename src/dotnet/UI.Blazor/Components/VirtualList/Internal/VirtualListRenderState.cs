namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public class VirtualListRenderState
{
    public long RenderIndex { get; set; }
    public VirtualListDataQuery Query { get; set; } = null!;

    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public int StartExpansion { get; set; }
    public int EndExpansion { get; set; }

    public bool HasVeryFirstItem { get; set; }
    public bool HasVeryLastItem { get; set; }

    public string? ScrollToKey { get; set; }

    public Dictionary<string, VirtualListRenderItem> Items { get; set; } = null!;
}
