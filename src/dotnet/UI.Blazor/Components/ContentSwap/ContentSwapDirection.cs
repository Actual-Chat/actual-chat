namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Picks the left/right variant of an effect from the direction a tab-like selection moved.
/// One instance per swap area, kept by the component that renders it.
/// </summary>
public sealed class ContentSwapDirection
{
    private int _lastIndex;

    public ContentSwapEffect Swipe(int index)
        // Called from the markup that also sets Key, because ContentSwap reads Effect in the very
        // parameter set that starts the swap - so the index recorded here has to be the one the
        // outgoing content was last rendered with.
        => MoveTo(index) ? ContentSwapEffect.SwipeLeft : ContentSwapEffect.SwipeRight;

    public ContentSwapEffect Wipe(int index)
        => MoveTo(index) ? ContentSwapEffect.WipeLeft : ContentSwapEffect.WipeRight;

    // Private methods

    private bool MoveTo(int index)
    {
        // Moving to a later tab sends the content left, so the incoming one arrives from the right
        var isForward = index >= _lastIndex;
        _lastIndex = index;
        return isForward;
    }
}
