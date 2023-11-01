using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class MauiApplyThemeHandler
{
    private const string BarsColor = "Theme_BarsColor";
    private string _lastAppliedColor = "";

    public static readonly MauiApplyThemeHandler Instance = new ();

    protected MauiApplyThemeHandler()
    { }

    public void TryRestoreLastTheme()
    {
        var barsColor = Preferences.Default.Get<string>(BarsColor, "");
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
            Preferences.Default.Set(BarsColor, themeBarColors);
            ApplyBarColors(themeBarColors);
        }
        catch {
            // Ignore
        }
    }

    private void ApplyBarColors(string sColors)
    {
        if (sColors.IsNullOrEmpty())
            return;

        _ = DispatchToBlazor(_ => {
            if (OrdinalEquals(sColors, _lastAppliedColor))
                return;

            try {
                var items = sColors.Split(";");
                var sTopBarColor = sColors;
                var sBottomBarColor = sColors;
                if (items.Length >= 2) {
                    sTopBarColor = items[0];
                    sBottomBarColor = items[1];
                }

                if (ApplyBarColors(sTopBarColor, sBottomBarColor))
                    _lastAppliedColor = sColors;
            }
            catch {
                // Ignore
            }
        });

    }

    protected virtual bool ApplyBarColors(string sTopBarColor, string sBottomBarColor)
    {
        var mainPage = App.Current.MainPage;
        if (mainPage == null)
            return false;

        mainPage.BackgroundColor = Color.FromArgb(sBottomBarColor);
        return true;

    }
}
