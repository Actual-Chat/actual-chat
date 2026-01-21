namespace ActualChat.Hosting;

public enum ServiceMode
{
    Disabled = 0,
    Local,
    Server,
    Client,
    Distributed,
}

public static class ServiceModeExt
{
    public static bool UsesImplementation(this ServiceMode serviceMode)
        => serviceMode is not (ServiceMode.Disabled or ServiceMode.Client);
}
