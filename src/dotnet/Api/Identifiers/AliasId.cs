using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Represents a user-friendly alias for entities like chats or places.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<AliasId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<AliasId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<AliasId>))]
[TypeConverter(typeof(StringLikeTypeConverter<AliasId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class AliasId : StringIdentifier, IStringIdentifier<AliasId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<AliasId>();
    private static readonly ILruCache<string, AliasId> Cache = CreateCache<AliasId>(256);

    public static readonly Alphabet Alphabet = Alphabet.AlphaNumeric.Symbols + "_-";

    [IgnoreDataMember]
    public string NormalizedValue => field ??= Value.ToLower();

    // Factories and constructors

    private AliasId(string value) : base(value)
    { }

    // Equality

    public bool Equals(AliasId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is AliasId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(AliasId? left, AliasId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(AliasId? left, AliasId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static AliasId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AliasId>(s);

    public static AliasId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static AliasId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out AliasId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length < 5 || !Alphabet.IsMatch(s))
            return false;

        result = new AliasId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
