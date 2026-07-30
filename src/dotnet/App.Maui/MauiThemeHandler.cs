using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
using Color = Microsoft.Maui.Graphics.Color;

namespace ActualChat.App.Maui;

public class MauiThemeHandler
{
    public static readonly MauiThemeHandler Instance =
#if ANDROID
        new AndroidThemeHandler();
#else
        new();
#endif

    private Theme? _theme;
    private string _colors = "";
    private string _serialized;
    private string _appliedColors = "";
    private ILogger? _log;

    protected ILogger Log => _log ??= StaticLog.Factory.CreateLogger(GetType());

    public string TopBarColor {
        get {
            // --background-01 (see getColors in theme.ts), restored from preferences in the ctor,
            // so it's known before the web app loads.
            var items = _colors.Split(';');
            return items[0].Trim();
        }
    }

    protected MauiThemeHandler()
    {
        _serialized = MauiPreferences.Theme;
        var parts = _serialized.Split('|');
        if (parts.Length == 2) {
            _theme = Enum.TryParse<Theme>(parts[0], false, out var v) ? v : null;
            _colors = parts[1];
        }
    }

    public void OnThemeChanged(ThemeInfo themeInfo)
    {
        _theme = themeInfo.Theme;
        _colors = themeInfo.Colors;
        var serialized = string.Join('|', themeInfo.Theme?.ToString("G") ?? "", themeInfo.Colors);
        if (serialized != _serialized) {
            _serialized = serialized;
            MauiPreferences.Theme = serialized;
        }
        Apply();
    }

    public void Apply(bool force = false)
    {
        if (force || MauiLoadingUI.WhenFirstSplashRemoved.IsCompleted)
            Apply(_theme, _colors, force);
    }

    // The WebView's viewport stays fixed at the size it had on the splash frame, so on cold start
    // the content is clipped and env(safe-area-inset-*) is stale until the first real native relayout
    // (a theme switch or device rotation). This forces that relayout once when the WebView is ready.
    public virtual void RequestRelayout()
    { }

    // Protected methods

    protected void Apply(Theme? theme, string colors, bool force = false)
    {
        if (colors.IsNullOrEmpty())
            return;

        BeginDispatchToMainThread(() => {
            if (!force && colors == _appliedColors)
                return;

            try {
                var items = colors.Split(";");
                var topBarColor = colors;
                var bottomBarColor = colors;
                if (items.Length >= 2) {
                    topBarColor = items[0].Trim();
                    bottomBarColor = items[1].Trim();
                }

                if (Apply(topBarColor, bottomBarColor, theme))
                    _appliedColors = colors;
            }
            catch (Exception e) {
                Log.LogWarning(e, "ApplyColors failed, colors: '{Colors}'", colors);
            }
        });
    }

    protected virtual bool Apply(string topBarColor, string bottomBarColor, Theme? theme)
    {
 #pragma warning disable CA1826
        var mainPage = App.Current.Windows.FirstOrDefault()?.Page;
 #pragma warning restore CA1826
        if (mainPage == null)
            return false;

        var style = theme switch {
            Theme.Light => StatusBarStyle.DarkContent,
            Theme.Ash => StatusBarStyle.DarkContent,
            Theme.Dark => StatusBarStyle.LightContent,
            _ => StatusBarStyle.Default,
        };

#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
            StatusBar.SetColor(Colors.Transparent);
        else
            StatusBar.SetColor(Color.FromArgb(topBarColor));
#else
        StatusBar.SetColor(Colors.Transparent);
#endif
        StatusBar.SetStyle(style);
        mainPage.BackgroundColor = Color.FromArgb(bottomBarColor);
        return true;
    }
}
