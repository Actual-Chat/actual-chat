using ActualChat.UI.Blazor.Services;
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

        SetNavigationBarColor(bottomBarColor);
        return true;
    }

    [UnconditionalSuppressMessage("Trimming",
        "CA1422: Call site is reachable on Android >= v.X, obsolete on >= v.Y",
        Justification = "Fine for Window.SetNavigationBarColor")]
    public static bool SetNavigationBarColor(string bottomBarColor)
    {
        var window = Window;
        if (window == null)
            return false;

        var androidColor = Android.Graphics.Color.ParseColor(bottomBarColor);
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q) {
            window.NavigationBarContrastEnforced = false;
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
        }
        else
            window.SetNavigationBarColor(androidColor);

        // Set navigation bar icon appearance (light/dark)
        var isNavBarDark = IsDark(androidColor);
        var wic = new WindowInsetsControllerCompat(window, window.DecorView);
        wic.AppearanceLightNavigationBars = !isNavBarDark;
        return true;
    }

    // Private methods

    private static bool IsDark(Android.Graphics.Color color) {
        var darkness = 1 - (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return darkness >= 0.5;
    }
}
