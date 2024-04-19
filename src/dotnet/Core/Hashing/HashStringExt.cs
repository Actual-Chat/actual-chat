namespace ActualChat.Hashing;

public static class HashStringExt
{
    public static HashString ToBase64HashString<THash>(this THash output, HashAlgorithm algorithm)
        where THash : struct, IHashOutput
        => new (algorithm, HashEncoding.Base64, output.Base64());

    public static IHashOutput ToHashOutput(this HashString hashString)
    {
        var encoding = hashString.Encoding;
        var output = CreateOutput(hashString.Algorithm);
        if (!CopyBytes(hashString, encoding, output.AsSpan<byte>()))
            throw StandardError.Internal($"Failed to copy bytes to hash output {output.GetType().Name}");
        return output;
    }

    private static IHashOutput CreateOutput(HashAlgorithm algorithm)
        => algorithm switch {
            HashAlgorithm.MD5 => new HashOutput16(),
            HashAlgorithm.SHA1 => new HashOutput20(),
            HashAlgorithm.SHA256 => new HashOutput32(),
            HashAlgorithm.Blake2s => new HashOutput32(),
            HashAlgorithm.Blake2b => new HashOutput64(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

    private static bool CopyBytes(HashString hashString, HashEncoding encoding, Span<byte> dest)
    {
        switch (encoding) {
        case HashEncoding.Base16:
            var bytes = Convert.FromHexString(hashString.Hash);
            if (bytes.Length > dest.Length)
                return false;

            bytes.CopyTo(dest);
            return true;
        case HashEncoding.Base64:
            return Convert.TryFromBase64String(hashString.Hash.Value, dest, out _);
        case HashEncoding.Base64Url:
            return Base64UrlEncoder.Decode(hashString.Hash).TryCopyTo(dest);
        default:
            throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null);
        }
    }
}
