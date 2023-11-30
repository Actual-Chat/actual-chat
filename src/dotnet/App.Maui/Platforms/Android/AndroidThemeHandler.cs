using ActualChat.UI.Blazor.Services;
using AndroidX.Core.View;

namespace ActualChat.App.Maui;

public class AndroidThemeHandler : MauiThemeHandler
{
    private static Android.Views.Window? Window => (Platform.CurrentActivity as MainActivity)?.Window;

    protected override bool Apply(string topBarColor, string bottomBarColor, Theme? theme)
    {
        var cTopBar = Android.Graphics.Color.ParseColor(topBarColor);
        var cBottomBar = Android.Graphics.Color.ParseColor(bottomBarColor);
        var window = Window;
        if (window == null)
            return false;

        // Set System bars colors
        // See https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-basics
        // I do it from here because I can not modify theme 'Maui.MainTheme'
        // which is applied after calling base.OnCreate.
        window.SetStatusBarColor(cTopBar);
        var wic = new WindowInsetsControllerCompat(window, window.DecorView);
        var isDarkStatusBar = IsDark(cTopBar);
        wic.AppearanceLightStatusBars = !isDarkStatusBar;
        window.SetNavigationBarColor(cBottomBar);
        return true;

        static bool IsDark(Android.Graphics.Color color) {
            var darkness = 1 - (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return darkness >= 0.5;
        }
    }
}
