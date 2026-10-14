using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Rpc;

/// <summary>
/// What a relay says about a client: the IP it saw the client connect from, signed with the
/// relay's key. A relay forwards TLS as-is, so this is the only way the origin learns the
/// client's IP. Wire format: v1|relayId|clientIP|unixSeconds|signature, the signature being
/// base64url HMAC-SHA256 of everything before it, keyed by the relay's key string.
/// </summary>
public sealed record RpcRelayAttestation(string RelayId, IPAddress ClientIP, DateTimeOffset IssuedAt)
{
    public const string QueryParameterName = "relayAttestation";
    public const int Port = 8443;
    public const string Version = "v1";
    private const int MaxLength = 256;
    private const char Separator = '|';
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(10);

    public static string Sign(string relayId, string clientIP, DateTimeOffset issuedAt, string key)
    {
        var payload = string.Join(Separator, Version, relayId, clientIP, issuedAt.ToUnixTimeSeconds());
        return payload + Separator + ComputeSignature(payload, key);
    }

    // The client only passes the value on, so it checks the shape and leaves the signature to the origin
    public static bool IsWellFormed(string? value)
        => TryParse(value, out _, out _, out _);

    public static bool TryVerify(
        string? value,
        Func<string, IReadOnlyList<string>> getKeys,
        DateTimeOffset now,
        [NotNullWhen(true)] out RpcRelayAttestation? result)
    {
        result = null;
        if (!TryParse(value, out var attestation, out var payload, out var signature))
            return false;

        var age = now - attestation.IssuedAt;
        if (age > MaxAge || age < -MaxClockSkew)
            return false;

        var isSignatureValid = false;
        foreach (var key in getKeys.Invoke(attestation.RelayId))
            // Every key is tried, so the time doesn't say which one matched
            isSignatureValid |= CryptographicOperations.FixedTimeEquals(
                ComputeSignatureBytes(payload, key), signature);
        if (!isSignatureValid)
            return false;

        result = attestation;
        return true;
    }

    // Private methods

    private static bool TryParse(
        string? value,
        [NotNullWhen(true)] out RpcRelayAttestation? attestation,
        [NotNullWhen(true)] out string? payload,
        [NotNullWhen(true)] out byte[]? signature)
    {
        attestation = null;
        payload = null;
        signature = null;
        if (value.IsNullOrEmpty() || value.Length > MaxLength)
            return false;

        var parts = value.Split(Separator);
        if (parts.Length != 5 || parts[0] != Version || parts[1].Length == 0
            || !IPAddress.TryParse(parts[2], out var clientIP)
            || !long.TryParse(parts[3], out var issuedAtSeconds))
            return false;

        try {
            signature = Base64Url.DecodeFromChars(parts[4]);
            attestation = new RpcRelayAttestation(
                parts[1], clientIP, DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds));
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException) {
            return false;
        }
        payload = value[..value.LastIndexOf(Separator)];
        return true;
    }

    private static string ComputeSignature(string payload, string key)
        => Base64Url.EncodeToString(ComputeSignatureBytes(payload, key));

    private static byte[] ComputeSignatureBytes(string payload, string key)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));
}
