namespace ActualChat.Hosting;

public enum ServiceMode
{
    Disabled = 0,
    Local,
    Server,
    Client,
    Hybrid,
}

public static class ServiceModeExt
{
    public static bool IsDisabled(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Disabled;
    public static bool IsLocal(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Local;
    public static bool IsServer(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Server;
    public static bool IsClient(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Client;
    public static bool RoutingServer(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Hybrid;
}
