using System.Net;
using System.Net.Sockets;
using ActualChat.Media.Module;
using ActualLab.Diagnostics;

namespace ActualChat.Media;

public class EgressGuard(HostInfo hostInfo, MediaSettings settings, ILogger<EgressGuard> log)
{
    private static readonly string[] DomainDenyListPrefix = [".local"];
    private ILogger? DebugLog => log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    private HostWildcard[] AllowedHostWildcards => field ??= [..settings.CrawlingHostAllowList.Select(x => new HostWildcard(x))];

    private IPNetwork[] SpecialSubnets => field ??= [..SpecialAddresses.Subnets.Union(settings.CrawlingCidrDenylist).Select(IPNetwork.Parse)];

    private string[] DomainDenyList => field ??= [
        ..DomainDenyListPrefix.Union(settings.CrawlingDomainDenylist, StringComparer.OrdinalIgnoreCase),
    ];

    public async Task<bool> IsAllowed(string host, CancellationToken cancellationToken = default)
    {
        if (IsDevelopmentInstanceBypassEnabled)
            return true;

        if (AllowedHostWildcards.Any(x => x.IsMatch(host)))
            return true;

        if (!IsAllowedHost(host))
            return false;

        var addresses = await Resolve(host, cancellationToken).ConfigureAwait(false);
        return addresses.Length != 0 && addresses.All(x => IsAllowedAddress(host, x));
    }

    public bool IsAllowedUri(Uri uri)
    {
        if (!EgressHttpHandler.IsHttpUri(uri))
            return false;

        if (IsDevelopmentInstanceBypassEnabled)
            return true;

        return IsAllowedHost(uri.DnsSafeHost);
    }

    public bool IsAllowedAddress(string host, IPAddress address)
    {
        if (IsDevelopmentInstanceBypassEnabled)
            return true;

        if (AllowedHostWildcards.Any(x => x.IsMatch(host)))
            return true;

        return IsAllowedHost(host) && IsAllowedIpAddress(address);
    }

    // Private methods

    private bool IsDevelopmentInstanceBypassEnabled
        => hostInfo is { IsDevelopmentInstance: true, IsTested: false };

    private bool IsAllowedHost(string host)
        => !IPAddress.TryParse(host, out _) && IsAllowedDomain(host);

    private bool IsAllowedDomain(string host)
        => !DomainDenyList.Any(domain => host.EndsWith(domain, StringComparison.OrdinalIgnoreCase));

    private async Task<IPAddress[]> Resolve(string host, CancellationToken cancellationToken)
    {
        try {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException) {
            DebugLog?.LogDebug("Failed to resolve host '{Host}'", host);
            return [];
        }
        catch (Exception e) {
            log.LogError(e, "Failed to resolve host '{Host}'", host);
            return [];
        }
    }

    private bool IsAllowedIpAddress(IPAddress address)
    {
        if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            return false;

        if (IPAddress.IsLoopback(address))
            return false;

        if (address.IsIPv6LinkLocal)
            return false;

        if (address.IsIPv6SiteLocal)
            return false;

        if (address.IsIPv6UniqueLocal)
            return false;

        return !SpecialSubnets.Any(x => x.Contains(address));
    }
}
