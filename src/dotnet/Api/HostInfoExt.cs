using System.Collections.ObjectModel;
using ActualChat.Hosting;

namespace ActualChat;

/// <summary>
/// Extension methods for <see cref="Hosting.HostInfo"/> URL and host resolution.
/// </summary>
public static class HostInfoExt
{
    public static string GetAllowedBaseUrl(this HostInfo hostInfo, string host, string scheme = "https")
        => hostInfo.GetHosts().Contains(host)
            ? new Uri($"{scheme}://{host}").ToBase().AbsoluteUri
            : hostInfo.BaseUrl;

    public static IReadOnlySet<string> GetOwnAndKnownHosts(this HostInfo hostInfo)
    {
        // GetHosts() is a static per-flavor set, so it misses worktree deployments
        // (wt1.local.voxt.ai, ...) — their own host comes from trusted configuration.
        var hosts = hostInfo.GetHosts();
        if (hostInfo.BaseUrl.IsNullOrEmpty())
            return hosts;

        var ownHost = hostInfo.BaseUrl.EnsureSuffix("/").ToUri().Host;
        return ownHost.IsNullOrEmpty() || hosts.Contains(ownHost)
            ? hosts
            : new HashSet<string>([..hosts, ownHost], StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> GetHosts(this HostInfo hostInfo)
        => hostInfo.BaseUrlKind switch {
            BaseUrlKind.Unknown => ReadOnlySet<string>.Empty,
            BaseUrlKind.Production => Constants.Hosts.AllProd,
            BaseUrlKind.Development => Constants.Hosts.AllDev,
            BaseUrlKind.Local => Constants.Hosts.AllLocal,
            _ => throw new ArgumentOutOfRangeException(nameof(hostInfo.BaseUrlKind)),
        };
}
