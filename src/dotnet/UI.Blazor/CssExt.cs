namespace ActualChat.UI.Blazor;

public static class CssExt
{
    public static string ToEnabledClass(this bool isEnabled)
        => isEnabled ? "" : "disabled";
}
