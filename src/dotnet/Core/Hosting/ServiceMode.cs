namespace ActualChat.Hosting;

/// <summary>
/// Defines how a service is deployed: local, client, server, or distributed.
/// </summary>
public enum ServiceMode
{
    Disabled = 0,
    Local,
    Server,
    Client,
    Distributed,
}

/// <summary>
/// Extension methods for <see cref="ServiceMode"/>.
/// </summary>
public static class ServiceModeExt
{
    public static bool UsesImplementation(this ServiceMode serviceMode)
        => serviceMode is not (ServiceMode.Disabled or ServiceMode.Client);
}
