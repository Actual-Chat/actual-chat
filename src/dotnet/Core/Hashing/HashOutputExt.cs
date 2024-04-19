namespace ActualChat.Hashing;

public static class HashOutputExt
{
    public static string Base16<THash>(this THash hash)
        where THash : struct, IHashOutput
        => Convert.ToHexString(hash.Bytes);
    public static string Base16<THash>(this THash hash, int count)
        where THash : struct, IHashOutput
        => Convert.ToHexString(hash.Bytes.Slice(0, count));

    public static string Base64<THash>(this THash hash)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes);
    public static string Base64<THash>(this THash hash, int count)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes.Slice(0, count));

    public static string Base64Url<THash>(this THash hash)
        where THash : struct, IHashOutput
        => Base64UrlEncoder.Encode(hash.Bytes);
    public static string Base64Url<THash>(this THash hash, int count)
        where THash : struct, IHashOutput
        => Base64UrlEncoder.Encode(hash.Bytes.Slice(0, count));

    public static string AlphaNumeric<THash>(this THash hash)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes).TrimEnd('=').Replace('+', '0').Replace('/', '1');
    public static string AlphaNumeric<THash>(this THash hash, int count)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes.Slice(0, count)).TrimEnd('=').Replace('+', '0').Replace('/', '1');

    public static HashOutput32 FromBase64(this string s)
    {
        var result = new HashOutput32();
        if (!Convert.TryFromBase64String(s, result.AsSpan<byte>(), out _))
            throw StandardError.Internal("Could not parse base64 string");

        return result;
    }
}
