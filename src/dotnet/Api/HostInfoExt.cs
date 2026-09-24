using System.Collections.ObjectModel;

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

    public static bool IsLocalDevInstance(this HostInfo hostInfo)
        // BaseUrlKind reads the host name, and a CI e2e run has no nginx or DNS for *.local.voxt.ai:
        // it serves the app straight from Kestrel on http://localhost:7080, which is local dev by
        // every measure except that name. EmailAuth.IsTestAgentEmailTotpHost already counts loopback.
        => hostInfo.IsDevelopmentInstance
            && (hostInfo.BaseUrlKind == BaseUrlKind.Local
                || hostInfo.BaseUrl.EnsureSuffix("/").ToUri().IsLoopback);

    public static IReadOnlySet<string> GetHosts(this HostInfo hostInfo)
        => hostInfo.BaseUrlKind switch {
            BaseUrlKind.Unknown => ReadOnlySet<string>.Empty,
            BaseUrlKind.Production => Constants.Hosts.AllProd,
            BaseUrlKind.Development => Constants.Hosts.AllDev,
            BaseUrlKind.Local => Constants.Hosts.AllLocal,
            _ => throw new ArgumentOutOfRangeException(nameof(hostInfo.BaseUrlKind)),
        };
}
