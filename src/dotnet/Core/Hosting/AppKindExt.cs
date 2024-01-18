namespace ActualChat.Hosting;

public static class AppKindExt
{
    public static bool IsMobile(this AppKind appKind)
        => appKind is AppKind.Ios or AppKind.Android;

    public static bool IsApple(this AppKind appKind)
        => appKind is AppKind.Ios or AppKind.MacOS;

    public static bool HasJit(this AppKind appKind)
        => appKind is AppKind.Android or AppKind.Windows;
}
