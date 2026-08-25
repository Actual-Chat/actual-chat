using ActualChat.UI;
using AndroidX.Core.View;

namespace ActualChat.App.Maui;

public class AndroidThemeHandler : MauiThemeHandler
{
    private static Android.Views.Window? Window => (Platform.CurrentActivity as MainActivity)?.Window;

    [UnconditionalSuppressMessage("Trimming",
        "CA1422: Call site is reachable on Android >= v.X, obsolete on >= v.Y",
        Justification = "Fine for Window.SetNavigationBarColor")]
    protected override bool Apply(string topBarColor, string bottomBarColor, Theme? theme)
    {
        // Call base for status bar handling via CommunityToolkit and background color
        if (!base.Apply(topBarColor, bottomBarColor, theme))
            return false;

        SetBarsAppearance(topBarColor, bottomBarColor);
        return true;
    }

    [UnconditionalSuppressMessage("Trimming",
        "CA1422: Call site is reachable on Android >= v.X, obsolete on >= v.Y",
        Justification = "Fine for Window.SetNavigationBarColor")]
    public static bool SetBarsAppearance(string topBarColor, string bottomBarColor)
    {
        var window = Window;
        if (window == null)
            return false;

        // Edge-to-edge: without this the window stays in fits-system-windows mode and the WebView
        // sees zero env(safe-area-inset-*). Enabling it here (called from OnCreate) makes insets
        // correct from the first layout instead of only after the first theme switch.
        WindowCompat.SetDecorFitsSystemWindows(window, false);

        var statusBarColor = Android.Graphics.Color.ParseColor(topBarColor);
        var navBarColor = Android.Graphics.Color.ParseColor(bottomBarColor);
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q) {
            window.NavigationBarContrastEnforced = false;
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
        }
        else
            window.SetNavigationBarColor(navBarColor);

        // Set status/navigation bar icon appearance (light/dark)
        var wic = new WindowInsetsControllerCompat(window, window.DecorView);
        wic.AppearanceLightStatusBars = !IsDark(statusBarColor);
        wic.AppearanceLightNavigationBars = !IsDark(navBarColor);
        return true;
    }

    public override void RequestRelayout()
    {
        var window = Window;
        if (window == null)
            return;

        var decorView = window.DecorView;
        decorView.Post(() => {
            ViewCompat.RequestApplyInsets(decorView);
            decorView.RequestLayout();
        });
    }

    // Private methods

    private static bool IsDark(Android.Graphics.Color color) {
        var darkness = 1 - (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return darkness >= 0.5;
    }
}
