namespace ActualChat.UI.Blazor.Components;

/// <summary> The data which is transferred from js to blazor. </summary>
public class VirtualListClientSideState
{
    public long RenderIndex { get; set; }

    /// <summary> Can we scroll programmatically at the moment? </summary>
    public bool IsSafeToScroll { get; set; }

    /// <summary> Scroll top of the viewport. </summary>
    public double ScrollTop { get; set; }

    /// <summary> Client height (visible viewport height) of the viewport. </summary>
    public double ClientHeight { get; set; }

    public Dictionary<string, double> ItemSizes { get; set; } = new(StringComparer.Ordinal);
}
