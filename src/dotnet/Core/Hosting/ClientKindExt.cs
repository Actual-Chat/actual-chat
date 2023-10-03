namespace ActualChat.Hosting;

public static class ClientKindExt
{
    public static bool IsMobile(this ClientKind clientKind)
        => clientKind is ClientKind.Ios or ClientKind.Android;
}
