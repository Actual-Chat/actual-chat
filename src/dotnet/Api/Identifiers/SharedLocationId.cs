using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

/// <summary>
/// Identifier of a <see cref="Chat.SharedLocation"/> — the record a chat entry
/// references via <see cref="Chat.ChatEntry.LocationId"/>.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661 // Overrides ==/Equals but not GetHashCode (provided by base)
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<SharedLocationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<SharedLocationId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<SharedLocationId>))]
[TypeConverter(typeof(StringLikeTypeConverter<SharedLocationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class SharedLocationId : StringIdentifier, IStringIdentifier<SharedLocationId>
{
    private static readonly ILruCache<string, SharedLocationId> Cache = CreateCache<SharedLocationId>(128);
    private static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    // Factories and constructors

    public static SharedLocationId New()
        => new (IdGenerator.Next());

    private SharedLocationId(string value) : base(value) { }

    // Equality

    public bool Equals(SharedLocationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is SharedLocationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(SharedLocationId? left, SharedLocationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(SharedLocationId? left, SharedLocationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static SharedLocationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<SharedLocationId>(s);

    public static SharedLocationId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static SharedLocationId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out SharedLocationId? result)
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

        result = new SharedLocationId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
