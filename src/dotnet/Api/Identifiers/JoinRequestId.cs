using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

/// <summary>
/// Identifier of a request to join a private chat or Place.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661 // Overrides ==/Equals but not GetHashCode (provided by base)
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<JoinRequestId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<JoinRequestId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<JoinRequestId>))]
[TypeConverter(typeof(StringLikeTypeConverter<JoinRequestId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class JoinRequestId : ContentId, IStringIdentifier<JoinRequestId>
{
    private static readonly ILruCache<string, JoinRequestId> Cache = CreateCache<JoinRequestId>(128);
    private static readonly RandomStringGenerator IdGenerator = new(12, Alphabet.AlphaNumeric);

    // Factories and constructors

    public static JoinRequestId New()
        => new (IdGenerator.Next());

    private JoinRequestId(string value) : base(value) { }

    // Equality

    public bool Equals(JoinRequestId? other)
        => other is not null
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is JoinRequestId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(JoinRequestId? left, JoinRequestId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(JoinRequestId? left, JoinRequestId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static JoinRequestId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<JoinRequestId>(s);

    public static JoinRequestId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static JoinRequestId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out JoinRequestId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length > 64 || !Alphabet.AlphaNumeric.IsMatch(s))
            return false;

        result = new JoinRequestId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
