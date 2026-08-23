namespace ActualChat.UI.Blazor.Components;

public static class ContentSwapLayerStateExt
{
    public static bool CanRender(this ContentSwapLayerState state)
        // A replacing layer has to keep the exact DOM it had - its scroll offsets and JS instances
        // are the ones on screen - so every render stops at it, including its own.
        => state is ContentSwapLayerState.Pending or ContentSwapLayerState.Displayed;

    public static bool IsVisible(this ContentSwapLayerState state)
        // Replacing is still what the user sees, so it keeps whatever it registered into a shared
        // area until the hand-off - a layer that is merely building must not claim one.
        => state is ContentSwapLayerState.Displayed or ContentSwapLayerState.Replacing;
}
