using System.Net;
using ActualChat.Module;

namespace ActualChat.Rpc;

/// <summary>
/// Finds the real client IP of a connection that came through a relay, from the attestation
/// the client got from that relay.
/// </summary>
public sealed class RpcRelayAttestationVerifier(CoreServerSettings settings, MomentClockSet clocks)
{
    private static readonly char[] EntrySeparators = [';', ','];
    private readonly Dictionary<string, string[]> _keys = ParseKeys(settings.RpcRelayKeys);

    public IPAddress? TryGetClientIP(string? attestation)
        => RpcRelayAttestation.TryVerify(attestation, GetKeys, clocks.SystemClock.UtcNow, out var result)
            ? result.ClientIP
            : null;

    // "kz1=key1;kz1=key2;tr1=key3": a relay can have several keys, which is how one is rotated
    public static Dictionary<string, string[]> ParseKeys(string value)
        => value
            .Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => (Index: x.IndexOf('='), Entry: x))
            .Where(x => x.Index > 0 && x.Index < x.Entry.Length - 1)
            .GroupBy(x => x.Entry[..x.Index], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Entry[(x.Index + 1)..]).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    // Private methods

    private IReadOnlyList<string> GetKeys(string relayId)
        => _keys.GetValueOrDefault(relayId) ?? [];
}
