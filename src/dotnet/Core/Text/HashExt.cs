using System.Text;

namespace ActualChat;

public static class HashExt
{
    // ReSharper disable once InconsistentNaming
    public static string GetSHA1HashCode(this string input, HashEncoding encoding = HashEncoding.Base16)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha1.ComputeHash(inputBytes);
        return hashBytes.EncodeHash(encoding);
    }

    // ReSharper disable once InconsistentNaming
    public static string GetSHA256HashCode(this string input, HashEncoding encoding = HashEncoding.Base16)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);
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
