namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Picks the directional variant of an effect from the direction a tab-like selection moved.
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

    public ContentSwapEffect WipeVertical(int index)
        // For an area whose entries are stacked rather than laid out in a rail: index 0 is the
        // topmost one, so a later entry sends the content up and the incoming one arrives from below.
        => MoveTo(index) ? ContentSwapEffect.WipeUp : ContentSwapEffect.WipeDown;

    // Private methods

    private bool MoveTo(int index)
    {
        // Moving to a later tab sends the content left, so the incoming one arrives from the right
        var isForward = index >= _lastIndex;
        _lastIndex = index;
        return isForward;
    }
}
