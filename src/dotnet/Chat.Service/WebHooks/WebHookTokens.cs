using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Chat;

public static class WebHookTokens
{
    private const int SecretLength = 32;
    private static readonly int SecretTextLength = Base64Url.GetEncodedLength(SecretLength);

    public static string New()
        => Constants.WebHooks.TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretLength));

    public static string Hash(string token)
        => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool LooksValid(string token)
    {
        // The length is exact rather than a minimum: it's the cheapest way to keep arbitrary
        // strings from reaching the GetByTokenHash lookup at all.
        var prefix = Constants.WebHooks.TokenPrefix;
        if (token.Length != prefix.Length + SecretTextLength || !token.StartsWith(prefix))
            return false;

        foreach (var c in token.AsSpan(prefix.Length))
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                return false;

        return true;
    }
}
