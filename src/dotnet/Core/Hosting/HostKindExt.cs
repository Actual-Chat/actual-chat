namespace ActualChat.Hosting;

public static class HostKindExt
{
    public static bool IsServer(this HostKind hostKind)
        => hostKind is HostKind.Server;

    public static bool IsApp(this HostKind hostKind)
        => hostKind is HostKind.WasmApp or HostKind.MauiApp;
    public static bool IsWasmApp(this HostKind hostKind)
        => hostKind is HostKind.WasmApp;
    public static bool IsMauiApp(this HostKind hostKind)
        => hostKind is HostKind.MauiApp;

    public static bool HasBlazorUI(this HostKind hostKind)
        => hostKind is not HostKind.Unknown;
}
