namespace ActualChat;

/// <summary>
/// A hex-digit key of <see cref="Size"/> digits; <see cref="Value"/> holds them in its lowest
/// bits, so a key and its <see cref="Head"/> route identically under positive modulo.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Serialization.Internal.ShardKeyMessagePackFormatter))]
public readonly partial record struct ShardKey : IStringLike<ShardKey>, IComparable<ShardKey>
{
    public const int MaxSize = 8;

    // Not default(ShardKey): that is the full-size zero key
    public static readonly ShardKey Empty = new(0u, 0);

    private static readonly string[] HexFormats = ["", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8"];
    private static readonly string[] OneDigitStrings
        = [.. Enumerable.Range(0, 16).Select(x => x.ToString(HexFormats[1]))];
    private static readonly string[] TwoDigitStrings
        = [.. Enumerable.Range(0, 256).Select(x => x.ToString(HexFormats[2]))];

    // Stored inverted, so default(ShardKey) is the full-size zero key rather than an empty one
    private readonly int _sizeComplement;

    [DataMember(Order = 0), Key(0)] public uint Value { get; }
    [DataMember(Order = 1), Key(1)] public int Size => MaxSize - _sizeComplement;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    string IStringLike.Value => ToString();

    public static ShardKey New(uint value) => new(value);
    public static ShardKey New(uint value, int size) => new(value, size);
    public static ShardKey New(int value) => new(unchecked((uint)value));
    public static ShardKey New(int value, int size) => new(unchecked((uint)value), size);
    public static ShardKey New(string? value) => New(value?.GetXxHash3() ?? 0);
    public static ShardKey New(string? value, int size) => New(value?.GetXxHash3() ?? 0, size);
    public static ShardKey New(Symbol value) => New(value.Value.GetXxHash3());
    public static ShardKey New(Symbol value, int size) => New(value.Value.GetXxHash3(), size);

    // Number of distinct keys of the given size; MaxSize is excluded - 16^8 overflows int
    public static long KeyCount(int size)
        => size switch {
            < 0 => throw new ArgumentOutOfRangeException(nameof(size)),
            <= MaxSize => 1L << (size << 2),
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

    public ShardKey(uint value)
    {
        _sizeComplement = 0;
        Value = value;
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public ShardKey(uint value, int size)
    {
        switch (size) {
        case < 0:
            throw new ArgumentOutOfRangeException(nameof(size));
        case < MaxSize:
            _sizeComplement = MaxSize - size;
            Value = value & ((1u << (size << 2)) - 1);
            break;
        case MaxSize:
            _sizeComplement = 0;
            Value = value;
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    // The highest `size` digits
    public ShardKey Head(int size)
    {
        if (size <= 0)
            return Empty;
        if (size >= Size)
            return this;

        return new ShardKey(Value >> ((Size - size) << 2), size);
    }

    // The lowest `size` digits
    public ShardKey Tail(int size)
    {
        if (size <= 0)
            return Empty;
        if (size >= Size)
            return this;

        return new ShardKey(Value, size);
    }

    public override string ToString()
        => Format(Value, Size);

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
        if (s.Length is < 1 or > MaxSize)
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

        result = new ShardKey(value, s.Length);
        return true;
    }

    // Private methods

    private static string Format(uint value, int size)
        => size switch {
            0 => "",
            1 => OneDigitStrings[value],
            2 => TwoDigitStrings[value],
            _ => value.ToString(HexFormats[size]),
        };
}
