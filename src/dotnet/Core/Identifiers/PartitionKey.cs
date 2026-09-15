using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract, MessagePackObject]
[JsonConverter(typeof(StringLikeJsonConverter<PartitionKey>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<PartitionKey>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<PartitionKey>))]
[TypeConverter(typeof(StringLikeTypeConverter<PartitionKey>))]
public readonly record struct PartitionKey : IStringLike<PartitionKey>, IComparable<PartitionKey>
{
    public const int HexDigitCount = 6;
    public const int Mask = 0xffffff;

    [DataMember(Order = 0)]
    public int Value { get; }

    string IStringLike.Value => ToString();

    public static PartitionKey New(string value)
        => new(value.GetXxHash3());

    public PartitionKey(int value)
        => Value = value & Mask;

    public int GetValue(int prefixLength)
        => GetValue(0, prefixLength);

    public int GetValue(int startIndex, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, HexDigitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, HexDigitCount - startIndex);

        var shift = (HexDigitCount - startIndex - length) * 4;
        var mask = (1 << (length * 4)) - 1;
        return (Value >> shift) & mask;
    }

    public override string ToString()
        => Value.ToString("x6");

    public string ToString(int prefixLength)
        => ToString(0, prefixLength);

    public string ToString(int startIndex, int length)
    {
        var value = GetValue(startIndex, length);
        return length == 0 ? "" : value.ToString($"x{length}");
    }

    public string ToString(Range range)
    {
        var (startIndex, length) = range.GetOffsetAndLength(HexDigitCount);
        return ToString(startIndex, length);
    }

    public int CompareTo(PartitionKey other)
        => Value.CompareTo(other.Value);

    public static PartitionKey Parse(string? s)
        => Parse(s, 0);

    public static PartitionKey Parse(string? s, int startIndex)
        => TryParse(s, startIndex, out var result) ? result : throw StandardError.Format<PartitionKey>(s);

    public static bool TryParse(string? s, out PartitionKey result)
        => TryParse(s, 0, out result);

    public static bool TryParse(string? s, int startIndex, out PartitionKey result)
    {
        result = default;
        if (s.IsNullOrEmpty() || startIndex is < 0 or >= HexDigitCount || s.Length > HexDigitCount - startIndex)
            return false;

        var value = 0;
        foreach (var c in s) {
            var digit = c switch {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;

            value = (value << 4) | digit;
        }

        result = new PartitionKey(value << ((HexDigitCount - startIndex - s.Length) * 4));
        return true;
    }
}
