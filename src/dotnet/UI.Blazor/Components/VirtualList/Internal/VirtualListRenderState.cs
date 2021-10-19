namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
public class VirtualListRenderState
{
    public long RenderIndex { get; set; }

    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public double ScrollHeight { get; set; }
    public Dictionary<string, double> ItemSizes { get; set; } = null!;

    public double ScrollTop { get; set; }
    public double ClientHeight { get; set; }

    public bool MustMeasure { get; set; }
    public bool MustScroll { get; set; }
    public bool NotifyWhenSafeToScroll { get; set; }
}
