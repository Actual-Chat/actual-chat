using System.Text;

namespace ActualChat.Hashing;

public static class HashExt
{
    // byte[] & spans

    public static HashInput Hash(this ReadOnlySpan<byte> input)
        => new(input);
    public static HashInput Hash(this byte[]? input)
        => new(input ?? ReadOnlySpan<byte>.Empty);

    // string

    public static HashInput Hash(this string input, Encoding? encoding = null)
        => new(input.Encode(encoding));

    // Symbol

    public static HashInput Hash(this Symbol input, Encoding? encoding = null)
        => new(input.Value.Encode(encoding));

    // ISymbolIdentifier

    public static HashInput Hash<TIdentifier>(this TIdentifier input, Encoding? encoding = null)
        where TIdentifier : struct, ISymbolIdentifier
        => new(input.Id.Value.Encode(encoding));

    // Stream

    public static StreamHashInput Hash(this Stream input) => new(input);

    // Xor
    public static THash BitwiseXor<THash>(this IEnumerable<THash> hashes)
        where THash : IHashOutput, IEquatable<THash>, new()
    {
        var result = new THash();
        var resultSpan = result.AsSpan<byte>();
        foreach (var input in hashes)
            BitwiseXorInPlace(input.Bytes, resultSpan);
        return result;
    }

    private static void BitwiseXorInPlace(ReadOnlySpan<byte> input, Span<byte> result)
    {
        for (int j = 0; j < input.Length; j++)
            result[j] ^= input[j];
    }
}
