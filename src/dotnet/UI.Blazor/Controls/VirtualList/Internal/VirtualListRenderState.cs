namespace ActualChat.UI.Blazor.Controls.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public class VirtualListRenderState
{
    public long RenderIndex { get; set; }

    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public double? ScrollHeight { get; set; }
    public double? ScrollTop { get; set; }
    public double? ViewportHeight { get; set; }

    public Dictionary<string, double> ItemSizes { get; set; } = null!;

    public bool MustScroll { get; set; }
    public bool NotifyWhenSafeToScroll { get; set; }
}
