namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light = 0, Dark }

public static class ThemeExt
{
    public static string ToCssClass(this Theme theme)
        => theme switch {
            Theme.Light => "theme-light",
            Theme.Dark => "theme-dark",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };
}
