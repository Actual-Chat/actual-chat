using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class MauiThemeHandler
{
    private const string PreferencesKey = "Theme_Colors";
    private string _lastAppliedColors = "";
    private ILogger? _log;

    public static readonly MauiThemeHandler Instance =
#if ANDROID
        new AndroidThemeHandler();
#else
        new();
#endif

    protected ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger(GetType());

    protected MauiThemeHandler()
    { }

    public void TryRestoreLastTheme()
    {
        var colors = Preferences.Default.Get<string>(PreferencesKey, "");
        ApplyColors(colors);
    }

    public void OnThemeChanged(Theme theme, string colors)
    {
        Preferences.Default.Set(PreferencesKey, colors);
        ApplyColors(colors);
    }

    private void ApplyColors(string colors)
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

                if (ApplyColors(topBarColor, bottomBarColor))
                    _lastAppliedColors = colors;
            }
            catch (Exception e) {
                Log.LogWarning(e, "ApplyColors failed, colors: {Colors}", colors);
            }
        });
    }

    protected virtual bool ApplyColors(string topBarColor, string bottomBarColor)
    {
        var mainPage = App.Current.MainPage;
        if (mainPage == null)
            return false;

        mainPage.BackgroundColor = Color.FromArgb(bottomBarColor);
        return true;
    }
}
