using ActualChat.UI;

namespace ActualChat.App.Maui;

public sealed class WindowsThemeHandler : MauiThemeHandler
{
    protected override bool Apply(ThemeColors colors, Theme? theme)
    {
        if (!base.Apply(colors, theme))
            return false;

        WindowConfigurator.UpdateTitleBar(colors.Navbar, colors.Text);
        return true;
    }

    // WinUI has no status bar: the toolkit's StatusBar throws NotSupportedException there,
    // which used to abort the whole Apply
    protected override void ApplyStatusBar(ThemeColors colors, Theme? theme)
    { }
}
