using ActualChat.UI.Blazor.Services;
using AndroidX.Core.View;

namespace ActualChat.App.Maui;

public class AndroidApplyThemeHandler
{
    private const string BarColors = "Theme_BarColors";
    private string _lastAppliedColors = "";

    private Android.Views.Window? Window => (Platform.CurrentActivity as MainActivity)?.Window;

    public static readonly AndroidApplyThemeHandler Instance = new ();

    private AndroidApplyThemeHandler()
    {
    }

    public void TryRestoreLastTheme()
    {
        var barsColor = Preferences.Default.Get<string>(BarColors, "");
        ApplyBarColors(barsColor);
    }

    public void OnApplyTheme(Theme theme)
        => _ = OnApplyThemeAsync();

    private async Task OnApplyThemeAsync()
    {
        try {
            var services = await WhenScopedServicesReady().ConfigureAwait(true);
            var themeUI = services.GetRequiredService<ThemeUI>();
            var themeBarColors = await themeUI.GetBarColors().ConfigureAwait(true);
            Preferences.Default.Set(BarColors, themeBarColors);
            ApplyBarColors(themeBarColors);
        }
        catch {
            // Ignore
        }
    }

    private void ApplyBarColors(string sColors)
    {
        if (string.IsNullOrEmpty(sColors))
            return;

        _ = MainThread.InvokeOnMainThreadAsync(() => {
            if (OrdinalEquals(sColors, _lastAppliedColors))
                return;

            try {
                var items = sColors.Split(";");
                var sTopBarColor = sColors;
                var sBottomBarColor = sColors;
                if (items.Length >= 2) {
                    sTopBarColor = items[0];
                    sBottomBarColor = items[1];
                }
                var topBarColor = Android.Graphics.Color.ParseColor(sTopBarColor);
                var bottomBarColor = Android.Graphics.Color.ParseColor(sBottomBarColor);
                var window = Window;
                if (window != null) {
                    // Set System bars colors
                    // See https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-basics
                    // I do it from here because I can not modify theme 'Maui.MainTheme'
                    // which is applied after calling base.OnCreate.
                    window.SetStatusBarColor(topBarColor);
                    var wic = new WindowInsetsControllerCompat(window, window.DecorView);
                    var isDarkStatusBar = IsColorDark(topBarColor);
                    wic.AppearanceLightStatusBars = !isDarkStatusBar;
                    window.SetNavigationBarColor(bottomBarColor);
                    _lastAppliedColors = sColors;
                }
            }
            catch {
                // Ignore
            }
        });

        bool IsColorDark(Android.Graphics.Color color) {
            var darkness = 1 - (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return darkness >= 0.5;
        }
    }
}
