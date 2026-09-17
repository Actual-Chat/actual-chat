using ActualChat.Geo;
using ActualChat.Module;

namespace ActualChat.Rpc;

/// <summary>
/// Decides which clients get the sized RPC probe: only those in countries where
/// the network may cap a connection, so everyone else pays for no measuring at all.
/// </summary>
public sealed class RpcProbePolicy(CoreServerSettings settings)
{
    private static readonly char[] Separators = [';', ','];
    private readonly HashSet<string> _countries = settings.RpcProbeCountries
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<bool> ShouldMeasure(string? ipAddress)
    {
        if (_countries.Contains("*"))
            return true;
        if (_countries.Count == 0 || ipAddress.IsNullOrEmpty())
            return false;

        var countryCode = await GeoIP.ToCountryCode(ipAddress).ConfigureAwait(false);
        return countryCode is not null && _countries.Contains(countryCode);
    }
}
