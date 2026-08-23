namespace ActualChat.UI.Blazor.Components;

public static class ContentSwapEffectExt
{
    public static string GetCssClass(this ContentSwapEffect effect)
        => effect switch {
            ContentSwapEffect.None => "c-swap-none",
            ContentSwapEffect.Fade => "c-swap-fade",
            ContentSwapEffect.Defocus => "c-swap-defocus",
            ContentSwapEffect.Reveal => "c-swap-reveal",
            ContentSwapEffect.Recede => "c-swap-recede",
            ContentSwapEffect.WipeRight => "c-swap-wipe c-swap-wipe-right",
            ContentSwapEffect.WipeLeft => "c-swap-wipe c-swap-wipe-left",
            ContentSwapEffect.WipeDown => "c-swap-wipe c-swap-wipe-down",
            ContentSwapEffect.WipeUp => "c-swap-wipe c-swap-wipe-up",
            ContentSwapEffect.SwipeRight => "c-swap-swipe c-swap-swipe-right",
            ContentSwapEffect.SwipeLeft => "c-swap-swipe c-swap-swipe-left",
            ContentSwapEffect.SwipeDown => "c-swap-swipe c-swap-swipe-down",
            ContentSwapEffect.SwipeUp => "c-swap-swipe c-swap-swipe-up",
            _ => "c-swap-fade",
        };
}
