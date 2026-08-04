using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Represents a phone number with country code and normalization support.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<Phone>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<Phone>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<Phone>))]
[TypeConverter(typeof(StringLikeTypeConverter<Phone>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class Phone : StringIdentifier, IStringIdentifier<Phone>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Phone>();
    private static readonly ILruCache<string, Phone> Cache = CreateCache<Phone>(256);

    public const char Delimiter = '-';

    [IgnoreDataMember]
    public string Code { get; }
    [IgnoreDataMember]
    public string Number { get; }
    [IgnoreDataMember]
    public string Hash => field ??= ContactIdExt.Hash(Value);
    [IgnoreDataMember]
    public string E164Value => field ??= $"+{Code}{Number}";

    // Factories and constructors

    public static Phone New(string code, string number)
        => new(Format(code, number), code, number);

    private Phone(string value, string code, string number) : base(value)
    {
        Code = code;
        Number = number;
    }

    // Normalization

    public bool IsNormalized()
        => IsPartNormalized(Code) && IsPartNormalized(Number);

    public Phone Normalize()
        => IsNormalized()
            ? this
            : New(NormalizePart(Code), NormalizePart(Number));

    public Phone RequireNormalized()
        => IsNormalized()
            ? this
            : throw StandardError.Format<Phone>(Value);

    // Equality

    public bool Equals(Phone? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is Phone other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Phone? left, Phone? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Phone? left, Phone? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(string code, string number)
        => $"{code}{Delimiter}{number}";

    public static Phone Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Phone>(s);

    public static Phone? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Phone? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Phone? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length > 32)
            return false;

        var idx = s.LastIndexOf(Delimiter);
        if (idx < 0)
            return false;

        var code = s[..idx];
        var number = s[(idx + 1)..];
        if (!code.All(char.IsDigit) || !number.All(char.IsDigit))
            return false;

        result = new Phone(s, code, number);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Helpers

    public static bool IsPartNormalized(string? phonePart)
    {
        if (phonePart.IsNullOrEmpty())
            return false;

        foreach (var c in phonePart)
            if (!char.IsDigit(c))
                return false;

        return true;
    }

    public static string NormalizePart(string phonePart)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var c in phonePart)
            if (char.IsDigit(c))
                sb.Append(c);
        return sb.ToStringAndRelease();
    }

    // Formatting

    public string ToReadable(bool withSpaces = true)
    {
        var space = withSpaces ? " " : "";
        var phoneCode = PhoneCodes.GetByCode(Code);
        if (phoneCode is null)
            return $"+{Code}{space}{Number}";

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        const int areaCodeLength = 3;
        const int defaultGroupSize = 3;
        sb.Append(phoneCode.DisplayCode);
        if (Number.Length < areaCodeLength)
            return sb.Append(Number).ToStringAndRelease();

        sb.Append(space).Append('(')
            .Append(Number.AsSpan(0, areaCodeLength))
            .Append(')').Append(space);

        var tailLength = ((Number.Length - areaCodeLength) % defaultGroupSize) switch {
            1 => 4, // tail: ...-11-11
            2 => 2, // tail: -11
            _ => 0, // no tail, all groups by three
        };

        Append(areaCodeLength, defaultGroupSize, tailLength);
        Append(Number.Length - tailLength, 2, 0);
        return sb.ToStringAndRelease();

        void Append(int startIdx, int groupSize, int skipTailLength)
        {
            for (int i = startIdx; i < Number.Length - skipTailLength; i += groupSize) {
                if (i != areaCodeLength)
                    sb.Append('-');
                sb.Append(Number.AsSpan(i, groupSize));
            }
        }
    }
}
