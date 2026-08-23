namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Where one <see cref="ContentSwap"/> layer is in the hand-off. Strictly monotonic: a layer only
/// ever moves forward. See <see cref="ContentSwapLayerStateExt"/> for what each state allows.
/// </summary>
public enum ContentSwapLayerState
{
    Pending = 0, // Rendering underneath the layer that's on screen, waiting for its turn
    Displayed, // What the user sees, and the only state that both renders and owns shared areas
    Replacing, // Still on screen and still owns them, but frozen: the swap that replaces it started
    Replaced, // The next layer took over; this one is animating out and about to be disposed
}
