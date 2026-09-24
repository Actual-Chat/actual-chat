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
        // every measure except that name. IsTestAgentTotpHost below already counts loopback.
        => hostInfo.IsDevelopmentInstance
            && (hostInfo.BaseUrlKind == BaseUrlKind.Local
                || hostInfo.BaseUrl.EnsureSuffix("/").ToUri().IsLoopback);

    public static bool IsTestAgentTotpHost(this HostInfo hostInfo)
    {
        // Where the fixed test-agent TOTP is honored: an integration test's own host, a loopback
        // URL (a CI e2e run), or a local.voxt.ai one. Nowhere else, and never on production.
        if (hostInfo.IsTested)
            return true;

        var baseUri = hostInfo.BaseUrl.ToUri();
        return baseUri.IsLoopback || Constants.Hosts.IsLocalDev(baseUri.Host);
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
