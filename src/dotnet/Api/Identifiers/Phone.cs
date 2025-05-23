using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using Cysharp.Text;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<Phone>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<Phone>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<Phone>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<Phone>))]
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
    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public string Hash => field ??= ContactIdExt.Hash(Value);
    [IgnoreDataMember] [field: AllowNull, MaybeNull]
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
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
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

    public static Phone Parse(string s)
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

        if (s.Length > 64)
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
        using var sb = ZString.CreateStringBuilder();
        foreach (var c in phonePart)
            if (char.IsDigit(c))
                sb.Append(c);
        return sb.ToString();
    }
}
