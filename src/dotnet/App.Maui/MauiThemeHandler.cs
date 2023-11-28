using ActualChat.UI.Blazor.Services;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
using Color = Microsoft.Maui.Graphics.Color;

namespace ActualChat.App.Maui;

public class MauiThemeHandler
{
    private const string PreferencesKey = "Theme_Colors";
    public static readonly MauiThemeHandler Instance =
#if ANDROID
        new AndroidThemeHandler();
#else
        new();
#endif

    private string _lastAppliedColors = "";
    private ILogger? _log;

    protected ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger(GetType());

    public void TryRestoreLastTheme()
    {
        var colors = Preferences.Default.Get<string>(PreferencesKey, "");
        _ = ApplyColorsAfterSplash(colors, null);
    }

    public void OnThemeChanged(ThemeInfo themeInfo)
    {
        Preferences.Default.Set(PreferencesKey, themeInfo.Colors);
        _ = ApplyColorsAfterSplash(themeInfo.Colors, themeInfo.Theme);
    }

    private async Task ApplyColorsAfterSplash(string colors, Theme? theme)
    {
        if (!LoadingUI.WhenSplashOverlayHidden.IsCompleted) {
            ApplyColors(MauiSettings.SplashBackgroundColor.ToArgbHex(true), theme);
            await LoadingUI.WhenSplashOverlayHidden.ConfigureAwait(false);
        }

        ApplyColors(colors, theme);
    }

    private void ApplyColors(string colors, Theme? theme)
    {
        if (colors.IsNullOrEmpty())
            return;

        MainThread.BeginInvokeOnMainThread(() => {
            if (OrdinalEquals(colors, _lastAppliedColors))
                return;

            try {
                var items = colors.Split(";");
                var topBarColor = colors;
                var bottomBarColor = colors;
                if (items.Length >= 2) {
                    topBarColor = items[0];
                    bottomBarColor = items[1];
                }

                if (ApplyColors(topBarColor, bottomBarColor, theme))
                    _lastAppliedColors = colors;
            }
            catch (Exception e) {
                Log.LogWarning(e, "ApplyColors failed, colors: {Colors}", colors);
            }
        });
    }

    protected virtual bool ApplyColors(string topBarColor, string bottomBarColor, Theme? theme)
    {
        var mainPage = App.Current.MainPage;
        if (mainPage == null)
            return false;

        var style = theme switch {
            Theme.Light => StatusBarStyle.DarkContent,
            Theme.Ash => StatusBarStyle.DarkContent,
            Theme.Dark => StatusBarStyle.LightContent,
            _ => StatusBarStyle.Default,
        };
        StatusBar.SetColor(Color.FromArgb(topBarColor));
        StatusBar.SetStyle(style);
        mainPage.BackgroundColor = Color.FromArgb(bottomBarColor);
        if (MauiWebView.Current is {} mauiWebView)
            mauiWebView.BlazorWebView.BackgroundColor = Color.FromArgb(bottomBarColor);

        return true;
    }
}
