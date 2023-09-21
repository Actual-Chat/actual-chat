namespace ActualChat.UI.Blazor.Services;

public static class ScreenSizeExt
{
    public static bool IsNarrow(this ScreenSize screenSize)
        => screenSize is ScreenSize.Small;

    public static bool IsWide(this ScreenSize screenSize)
        => !screenSize.IsNarrow();
}
