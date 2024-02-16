namespace ActualChat;

public enum ServiceMode
{
    SelfHosted = 0,
    Server,
    Client,
}

public static class ServiceModeExt
{
    public static bool IsClient(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Client;
    public static bool IsServer(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.Server;
    public static bool IsSelfHosted(this ServiceMode serviceMode)
        => serviceMode == ServiceMode.SelfHosted;
}
