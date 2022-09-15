namespace ActualChat.UI.Blazor.Components;

public static class ScreenSizeExt
{
    public static bool IsUnknown(this ScreenSize screenSize)
        => screenSize is ScreenSize.Unknown;

    public static bool IsOrLarger(this ScreenSize screenSize, ScreenSize minSize)
        => (int) screenSize >= (int) minSize;

    public static bool IsOrSmaller(this ScreenSize screenSize, ScreenSize minSize)
        => (int) screenSize <= (int) minSize;

    public static bool IsMobile(this ScreenSize screenSize)
        => screenSize == ScreenSize.Small;

    public static bool IsDesktop(this ScreenSize screenSize)
        => !screenSize.IsMobile();
}
