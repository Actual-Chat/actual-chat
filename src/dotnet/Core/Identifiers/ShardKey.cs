namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Serialization.Internal.ShardKeyMessagePackFormatter))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public readonly partial record struct ShardKey(
    [property: DataMember(Order = 0), Key(0)] uint Value)
    : IHasShardKey, IStringLike<ShardKey>, IComparable<ShardKey>
{
    public const int HexDigitCount = 8;

    private static readonly string[] HexFormats = ["", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8"];
    private static readonly string[] OneDigitStrings = Enumerable.Range(0, 16)
        .Select(x => x.ToString(HexFormats[1])).ToArray();
    private static readonly string[] TwoDigitStrings = Enumerable.Range(0, 256)
        .Select(x => x.ToString(HexFormats[2])).ToArray();

    ShardKey IHasShardKey.ShardKey => this;
    string IStringLike.Value => ToString();

    public static ShardKey New(uint value) => new(value);
    public static ShardKey New(int value) => new(unchecked((uint)value));
    public static ShardKey New(string? value) => value is null ? default : New(value.GetXxHash3());

    public uint GetValue(int prefixLength)
        => GetValue(0, prefixLength);

    public uint GetValue(int startIndex, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, HexDigitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, HexDigitCount - startIndex);
        if (length == 0)
            return 0;

        var shift = (HexDigitCount - startIndex - length) * 4;
        var mask = uint.MaxValue >> ((HexDigitCount - length) * 4);
        return (Value >> shift) & mask;
    }

    public override string ToString()
        => Value.ToString(HexFormats[HexDigitCount]);

    public string ToString(int prefixLength)
        => ToString(0, prefixLength);

    public string ToString(int startIndex, int length)
    {
        var value = GetValue(startIndex, length);
        return length switch {
            0 => "",
            1 => OneDigitStrings[value],
            2 => TwoDigitStrings[value],
            _ => value.ToString(HexFormats[length]),
        };
    }

    public string ToString(Range range)
    {
        var (startIndex, length) = range.GetOffsetAndLength(HexDigitCount);
        return ToString(startIndex, length);
    }

    public int CompareTo(ShardKey other)
        => Value.CompareTo(other.Value);

    public static ShardKey Parse(string? s)
        => Parse(s, 0);

    public static ShardKey Parse(string? s, int startIndex)
        => TryParse(s, startIndex, out var result) ? result : throw StandardError.Format<ShardKey>(s);

    public static bool TryParse(string? s, out ShardKey result)
        => TryParse(s, 0, out result);

    public static bool TryParse(string? s, int startIndex, out ShardKey result)
    {
        result = default;
        if (s.IsNullOrEmpty() || startIndex is < 0 or >= HexDigitCount || s.Length > HexDigitCount - startIndex)
            return false;

        var value = 0u;
        foreach (var c in s) {
            var digit = c switch {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;

            value = (value << 4) | (uint)digit;
        }

        result = new ShardKey(value << ((HexDigitCount - startIndex - s.Length) * 4));
        return true;
    }
}
