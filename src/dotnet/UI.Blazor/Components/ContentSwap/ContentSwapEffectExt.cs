namespace ActualChat.UI.Blazor.Components;

public static class ContentSwapEffectExt
{
    public static string GetCssClass(this ContentSwapEffect effect)
        => effect switch {
            ContentSwapEffect.None => "swap-none",
            ContentSwapEffect.Fade => "swap-fade",
            ContentSwapEffect.Defocus => "swap-defocus",
            ContentSwapEffect.Reveal => "swap-reveal",
            ContentSwapEffect.Recede => "swap-recede",
            ContentSwapEffect.WipeRight => "swap-wipe swap-wipe-right",
            ContentSwapEffect.WipeLeft => "swap-wipe swap-wipe-left",
            ContentSwapEffect.WipeDown => "swap-wipe swap-wipe-down",
            ContentSwapEffect.WipeUp => "swap-wipe swap-wipe-up",
            ContentSwapEffect.SwipeRight => "swap-swipe swap-swipe-right",
            ContentSwapEffect.SwipeLeft => "swap-swipe swap-swipe-left",
            ContentSwapEffect.SwipeDown => "swap-swipe swap-swipe-down",
            ContentSwapEffect.SwipeUp => "swap-swipe swap-swipe-up",
            _ => "swap-fade",
        };
}
