using System.Collections.ObjectModel;
using ActualChat.Hosting;

namespace ActualChat;

public static class HostInfoExt
{
    public static string GetAllowedBaseUrl(this HostInfo hostInfo, string host, string scheme = "https")
        => hostInfo.GetHosts().Contains(host)
            ? new Uri($"{scheme}://{host}").ToBase().AbsoluteUri
            : hostInfo.BaseUrl;

    public static IReadOnlySet<string> GetHosts(this HostInfo hostInfo)
        => hostInfo.BaseUrlKind switch {
            BaseUrlKind.Unknown => ReadOnlySet<string>.Empty,
            BaseUrlKind.Production => Constants.Hosts.AllProd,
            BaseUrlKind.Development => Constants.Hosts.AllDev,
            BaseUrlKind.Local => Constants.Hosts.AllLocal,
            _ => throw new ArgumentOutOfRangeException(),
        };
}
