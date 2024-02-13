using ActualChat.Hosting;

namespace ActualChat;

public static class HostInfoExt
{
    public static ServiceMode GetServiceMode(this HostInfo hostInfo, HostRole role)
        => hostInfo.HasRole(HostRole.SingleServer)
            ? ServiceMode.SelfHosted
            : hostInfo.HasRole(role)
                ? ServiceMode.Server
                : ServiceMode.Client;

    public static ServiceMode GetServiceMode(this HostInfo hostInfo, IEnumerable<HostRole> roles)
    {
        if (hostInfo.HasRole(HostRole.SingleServer))
            return ServiceMode.SelfHosted;

        foreach (var role in roles)
            if (hostInfo.HasRole(role))
                return ServiceMode.Server;

        return ServiceMode.Client;
    }
}
