namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// The animation <see cref="ContentSwap"/> plays when its content is replaced.
/// <see cref="ContentSwapEffectExt.GetCssClass"/> maps every member to the CSS that implements it.
/// </summary>
public enum ContentSwapEffect
{
    None = 0, // An instant cut at the hand-off - still a swap, because the hold is what makes it one
    Fade, // The outgoing content fades out
    Defocus, // ... and is blurred while it does, so it stops competing for the eye as readable text
    Reveal, // ... while the incoming content shows through it blurred and sharpens as it goes
    Recede, // ... shrinking and blurring as it goes, while the incoming content advances into place
    WipeRight, // A soft dissolve front sweeps across the outgoing content, left to right
    WipeLeft, // ... right to left
    WipeDown, // ... top to bottom
    WipeUp, // ... bottom to top
    SwipeRight, // Both layers slide right as one, the incoming content pushing the outgoing one out
    SwipeLeft, // ... left
    SwipeDown, // ... down
    SwipeUp, // ... up
}
