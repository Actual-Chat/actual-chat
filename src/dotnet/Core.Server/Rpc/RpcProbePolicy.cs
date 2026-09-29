using ActualChat.Geo;
using ActualChat.Module;

namespace ActualChat.Rpc;

/// <summary>
/// Decides which clients get the sized RPC probe: those in countries where the network
/// may cap a connection, and anyone measuring an edge relay - so everyone else pays for
/// no measuring at all.
/// </summary>
public sealed class RpcProbePolicy(CoreServerSettings settings)
{
    public const string EdgeHostInfix = ".edge.";
    private static readonly char[] Separators = [';', ','];
    private readonly HashSet<string> _countries = settings.RpcProbeCountries
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<bool> ShouldMeasure(string? ipAddress, string? host)
    {
        // The relays forward TLS as-is, so a relayed request arrives from the relay's own
        // address, in the relay's country. The host is the only thing that still says the
        // client came through a relay - and it only does so to measure it.
        if (IsEdgeHost(host))
            return true;
        if (_countries.Contains("*"))
            return true;
        if (_countries.Count == 0 || ipAddress.IsNullOrEmpty())
            return false;

        var countryCode = await GeoIP.ToCountryCode(ipAddress).ConfigureAwait(false);
        return countryCode is not null && _countries.Contains(countryCode);
    }

    public static bool IsEdgeHost(string? host)
        => host is not null && host.IndexOf(EdgeHostInfix, StringComparison.OrdinalIgnoreCase) > 0;
}
