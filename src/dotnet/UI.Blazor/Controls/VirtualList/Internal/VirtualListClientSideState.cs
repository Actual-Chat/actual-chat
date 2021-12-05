namespace ActualChat.UI.Blazor.Controls.Internal;

/// <summary> The data transferred from JS to Blazor. </summary>
public class VirtualListClientSideState
{
    public long RenderIndex { get; set; }

    /// <summary> Spacer size. </summary>
    public double SpacerSize { get; set; }
    /// <summary> End spacer size. </summary>
    public double EndSpacerSize { get; set; }
    /// <summary> Total height of the scroll area. </summary>
    public double ScrollHeight { get; set; }
    /// <summary> Size of resized items. </summary>
    public Dictionary<string, double> ItemSizes { get; set; } = new(StringComparer.Ordinal);

    /// <summary> Scroll top of the viewport. </summary>
    public double ScrollTop { get; set; }
    /// <summary> Client height (visible viewport height) of the viewport. </summary>
    public double ClientHeight { get; set; }

    /// <summary> Can we scroll programmatically at the moment? </summary>
    public bool IsSafeToScroll { get; set; }
    /// <summary> Was the list resized? </summary>
    public bool IsListResized { get; set; }
    /// <summary> Was the viewport changed? </summary>
    public bool IsViewportChanged { get; set; }
    /// <summary> Was the scroll initiated by user? </summary>
    public bool IsUserScrollDetected { get; set; }
}
