using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Security;

/// <summary>
/// Standard Webhooks (standardwebhooks.com) signing: HMAC-SHA256 over
/// "{id}.{timestamp}.{body}" with a "whsec_"-prefixed base64 secret,
/// rendered as "v1,<base64>".
/// </summary>
public static class StandardWebhookSigner
{
    public const string SecretPrefix = "whsec_";
    private const string Version = "v1,";

    public static string NewSecret()
        => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Sign(string secret, string id, long unixTimestamp, string body)
    {
        var key = Convert.FromBase64String(secret[SecretPrefix.Length..]);
        var payload = Encoding.UTF8.GetBytes($"{id}.{unixTimestamp}.{body}");
        return Version + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    }

    public static string SignatureHeader(
        string id, long unixTimestamp, string body, params ReadOnlySpan<string> secrets)
    {
        var parts = new string[secrets.Length];
        for (var i = 0; i < secrets.Length; i++)
            parts[i] = Sign(secrets[i], id, unixTimestamp, body);

        return string.Join(' ', parts);
    }

    public static bool Verify(
        string secret,
        string id,
        long unixTimestamp,
        string body,
        string header,
        TimeSpan tolerance,
        Moment now)
    {
        var age = now - (Moment.EpochStart + TimeSpan.FromSeconds(unixTimestamp));
        if (age.Duration() > tolerance)
            return false;

        var expected = Sign(secret, id, unixTimestamp, body);
        foreach (var part in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(part),
                    Encoding.UTF8.GetBytes(expected)))
                return true;

        return false;
    }
}
