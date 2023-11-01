using AndroidX.Core.View;
using Microsoft.Maui.Controls.PlatformConfiguration;

namespace ActualChat.App.Maui;

public class AndroidApplyThemeHandler : MauiApplyThemeHandler
{
    private Android.Views.Window? Window => (Platform.CurrentActivity as MainActivity)?.Window;

    public static new readonly AndroidApplyThemeHandler Instance = new ();

    private AndroidApplyThemeHandler()
    { }

    protected override bool ApplyBarColors(string sTopBarColor, string sBottomBarColor)
    {
        var topBarColor = Android.Graphics.Color.ParseColor(sTopBarColor);
        var bottomBarColor = Android.Graphics.Color.ParseColor(sBottomBarColor);
        var window = Window;
        if (window == null)
            return false;

        // Set System bars colors
        // See https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-basics
        // I do it from here because I can not modify theme 'Maui.MainTheme'
        // which is applied after calling base.OnCreate.
        window.SetStatusBarColor(topBarColor);
        var wic = new WindowInsetsControllerCompat(window, window.DecorView);
        var isDarkStatusBar = IsColorDark(topBarColor);
        wic.AppearanceLightStatusBars = !isDarkStatusBar;
        window.SetNavigationBarColor(bottomBarColor);
        return true;

        bool IsColorDark(Android.Graphics.Color color) {
            var darkness = 1 - (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return darkness >= 0.5;
        }
    }
}
