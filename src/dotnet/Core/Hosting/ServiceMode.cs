namespace ActualChat.Hosting;

public enum ServiceMode
{
    None = 0,
    Local,
    Server,
    Client,
    RoutingServer,
}

public static class ServiceModeExt
{
    public static bool IsNone(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.None;
    public static bool IsLocal(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Local;
    public static bool IsServer(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Server;
    public static bool IsClient(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Client;
    public static bool RoutingServer(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.RoutingServer;
}
