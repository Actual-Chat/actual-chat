using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<PlaceId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PlaceId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<PlaceId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PlaceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PlaceId : StringIdentifier, IStringIdentifier<PlaceId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceId>();
    private static readonly ILruCache<string, PlaceId> Cache = CreateCache<PlaceId>(32);

    public static readonly RandomStringGenerator IdGenerator = ChatId.IdGenerator;

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public PlaceChatId RootChatId => field ??= PlaceChatId.Parse(PlaceChatId.Format(this, Value));

    // Factories and constructors

    public static PlaceId New()
        => new(IdGenerator.Next());

    private PlaceId(string value) : base(value)
    { }

    // Equality

    public bool Equals(PlaceId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PlaceId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PlaceId? left, PlaceId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PlaceId? left, PlaceId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static PlaceId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceId>(s);

    public static PlaceId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static PlaceId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PlaceId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length is < 10 or > 64)
            return false;

        if (!(Alphabet.AlphaNumeric.IsMatch(s) || OrdinalEquals(s, "chat-roulette")))
            return false;

        result = new PlaceId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
