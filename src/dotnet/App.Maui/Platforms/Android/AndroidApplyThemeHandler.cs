using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class AndroidApplyThemeHandler
{
    private const string BarsColor = "Theme_BarsColor";
    private string _lastAppliedColor = "";

    private Android.Views.Window? Window => (Platform.CurrentActivity as MainActivity)?.Window;

    public static readonly AndroidApplyThemeHandler Instance = new ();

    private AndroidApplyThemeHandler()
    {
    }

    public void TryRestoreLastTheme()
    {
        var barsColor = Preferences.Default.Get<string>(BarsColor, "");
        ApplyBarsColor(barsColor);
    }

    public void OnApplyTheme(Theme theme)
        => _ = OnApplyThemeAsync();

    private async Task OnApplyThemeAsync()
    {
        try {
            var services = await WhenScopedServicesReady().ConfigureAwait(true);
            var themeUI = services.GetRequiredService<ThemeUI>();
            var themePostPanelColor = await themeUI.GetPostPanelColor().ConfigureAwait(true);
            Preferences.Default.Set(BarsColor, themePostPanelColor);
            ApplyBarsColor(themePostPanelColor);
        }
        catch {
            // Ignore
        }
    }

    private void ApplyBarsColor(string sColor)
    {
        if (string.IsNullOrEmpty(sColor))
            return;

        if (OrdinalEquals(sColor, _lastAppliedColor))
            return;

        try {
            var color = Android.Graphics.Color.ParseColor(sColor);
            var window = Window;
            if (window != null) {
                // Set System bars colors
                // See https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-basics
                // I do it from here because I can not modify theme 'Maui.MainTheme'
                // which is applied after calling base.OnCreate.
                window.SetStatusBarColor(color);
                window.SetNavigationBarColor(color);
                _lastAppliedColor = sColor;
            }
        }
        catch {
            // Ignore
        }
    }
}
