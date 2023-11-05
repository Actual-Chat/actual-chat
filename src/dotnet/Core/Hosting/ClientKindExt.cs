namespace ActualChat.Hosting;

public static class ClientKindExt
{
    public static bool IsMobile(this ClientKind clientKind)
        => clientKind is ClientKind.Ios or ClientKind.Android;
    public static bool HasJit(this ClientKind clientKind)
        => clientKind is ClientKind.Android or ClientKind.Windows;
}
