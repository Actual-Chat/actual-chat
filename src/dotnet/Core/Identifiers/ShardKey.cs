namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Serialization.Internal.ShardKeyMessagePackFormatter))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public readonly partial record struct ShardKey(
    [property: DataMember(Order = 0), Key(0)] uint Value
    ) : IStringLike<ShardKey>, IComparable<ShardKey>
{
    public const int MaxDigitCount = 8;

    private static readonly string[] HexFormats = ["", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8"];
    private static readonly string[] OneDigitStrings = Enumerable.Range(0, 16)
        .Select(x => x.ToString(HexFormats[1])).ToArray();
    private static readonly string[] TwoDigitStrings = Enumerable.Range(0, 256)
        .Select(x => x.ToString(HexFormats[2])).ToArray();

    string IStringLike.Value => ToString();

    public static ShardKey New(uint value) => new(value);
    public static ShardKey New(int value) => new(unchecked((uint)value));
    public static ShardKey New(string? value) => value is null ? default : New(value.GetXxHash3());
    public static ShardKey New(Symbol value) => New(value.Value.GetXxHash3());

    public uint GetValue(int digitCount)
    {
        if (digitCount <= 0)
            return 0;

        digitCount = Math.Min(MaxDigitCount, digitCount);

        var shift = (MaxDigitCount - digitCount) << 2;
        return Value >> shift;
    }

    public override string ToString()
        => Value.ToString(HexFormats[MaxDigitCount]);

    public string ToString(int digitCount)
    {
        digitCount = Math.Clamp(digitCount, 0, MaxDigitCount);
        var value = GetValue(digitCount);
        return digitCount switch {
            0 => "",
            1 => OneDigitStrings[value],
            2 => TwoDigitStrings[value],
            _ => value.ToString(HexFormats[digitCount]),
        };
    }

    public int CompareTo(ShardKey other)
        => Value.CompareTo(other.Value);

    public static ShardKey Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ShardKey>(s);

    public static ShardKey Parse(ReadOnlySpan<char> s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ShardKey>(s.ToString());

    public static bool TryParse(string? s, out ShardKey result)
        => TryParse(s.AsSpan(), out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ShardKey result)
    {
        result = default;
        if (s.Length is < 1 or > MaxDigitCount)
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

        result = new ShardKey(value << ((MaxDigitCount - s.Length) * 4));
        return true;
    }
}
