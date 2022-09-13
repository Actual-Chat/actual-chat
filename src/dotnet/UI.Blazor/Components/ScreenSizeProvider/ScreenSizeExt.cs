namespace ActualChat.UI.Blazor.Components;

public static class ScreenSizeExt
{
    public static bool IsUnknown(this ScreenSize sizeKind)
        => sizeKind == ScreenSize.Unknown;

    public static bool IsMobile(this ScreenSize sizeKind)
        => sizeKind == ScreenSize.Small;

    public static bool IsDesktop(this ScreenSize sizeKind)
        => !sizeKind.IsUnknown() && !sizeKind.IsMobile();

    public static bool IsMobileOrUnknown(this ScreenSize sizeKind)
        => sizeKind.IsUnknown() || sizeKind.IsMobile();

    public static bool IsDesktopOrUnknown(this ScreenSize sizeKind)
        => sizeKind.IsUnknown() || !sizeKind.IsDesktop();
}
