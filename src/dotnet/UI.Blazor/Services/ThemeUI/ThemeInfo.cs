namespace ActualChat.UI.Blazor.Services;

public sealed record ThemeInfo(
    Theme? Theme = null,
    Theme DefaultTheme = Theme.Light,
    Theme CurrentTheme = Theme.Light,
    string Colors = "")
{
    public static ThemeInfo Default = new();
}
