using ActualChat.Hosting;

namespace ActualChat;

public static class HostInfoExt
{
    public static ServiceMode GetServiceMode(this HostInfo hostInfo, HostRole serverRole)
    => hostInfo.HasRole(HostRole.SingleServer)
        ? ServiceMode.SelfHosted
        : hostInfo.HasRole(serverRole)
            ? ServiceMode.Server
            : ServiceMode.Client;
}
