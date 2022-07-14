namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public class VirtualListRenderState
{
    public long RenderIndex { get; set; }
//
    public VirtualListDataQuery Query { get; set; }

    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public int StartExpansion { get; set; }
    public int EndExpansion { get; set; }

//     public double? ScrollHeight { get; set; }
//     public double? ScrollTop { get; set; }
//     public double? ViewportHeight { get; set; }
    public bool HasVeryFirstItem { get; set; }
    public bool HasVeryLastItem { get; set; }

    public string? ScrollToKey { get; set; }
    // public bool UseSmoothScroll { get; set; }
//
//     public Dictionary<string, double> ItemSizes { get; set; } = null!;
//     public bool HasUnmeasuredItems { get; set; }
//     public VirtualListStickyEdgeState? StickyEdge { get; set; }

     public Dictionary<string, VirtualListRenderItem> RenderItems { get; set; } = null!;
}
