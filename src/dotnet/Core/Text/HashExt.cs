using System.Text;

namespace ActualChat;

public static class HashExt
{
    // ReSharper disable once InconsistentNaming
    public static string GetSHA1HashCode(this string input, HashEncoding encoding = HashEncoding.Base16)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
#pragma warning disable CA5350
        var hashBytes = System.Security.Cryptography.SHA1.HashData(inputBytes);
#pragma warning restore CA5350
        return hashBytes.EncodeHash(encoding);
    }

    // ReSharper disable once InconsistentNaming
    public static string GetSHA256HashCode(this string input, HashEncoding encoding = HashEncoding.Base16)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        return inputBytes.GetSHA256HashCode(encoding);
    }

    // ReSharper disable once InconsistentNaming
    public static string GetSHA256HashCode(this byte[] inputBytes, HashEncoding encoding = HashEncoding.Base16)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);
        return hashBytes.EncodeHash(encoding);
    }

    // ReSharper disable once InconsistentNaming
    public static async Task<string> GetSHA256HashCode(this Stream inputStream, HashEncoding encoding = HashEncoding.Base16)
    {
        var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(inputStream).ConfigureAwait(false);
        return hashBytes.EncodeHash(encoding);
    }

    public static string EncodeHash(this byte[] bytes, HashEncoding encoding)
        => encoding switch {
            HashEncoding.Base16 => Convert.ToBase64String(bytes),
            HashEncoding.Base64 => Convert.ToBase64String(bytes),
            HashEncoding.AlphaNumeric => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '0').Replace('/', '1'),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null),
        };
}
