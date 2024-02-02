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

    public static string AlphaNumeric<THash>(this THash hash)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes).TrimEnd('=').Replace('+', '0').Replace('/', '1');
    public static string AlphaNumeric<THash>(this THash hash, int count)
        where THash : struct, IHashOutput
        => Convert.ToBase64String(hash.Bytes.Slice(0, count)).TrimEnd('=').Replace('+', '0').Replace('/', '1');
}
