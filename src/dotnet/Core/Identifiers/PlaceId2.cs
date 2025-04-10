using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<PlaceId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PlaceId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PlaceId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class PlaceId2 : StringIdentifier, IStringIdentifier<PlaceId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceId2>();
    private static readonly ILruCache<string, PlaceId2> Cache = CreateCache<PlaceId2>(32);

    public static readonly RandomStringGenerator IdGenerator = ChatId2.IdGenerator;

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public PlaceChatId2 RootChatId => field ??= PlaceChatId2.Parse(PlaceChatId2.Format(this, this.Value));

    // Factories and constructors

    public static PlaceId2 New()
        => new(IdGenerator.Next());

    private PlaceId2(string value) : base(value)
    { }

    // Equality

    public bool Equals(PlaceId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PlaceId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PlaceId2? left, PlaceId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PlaceId2? left, PlaceId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static PlaceId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out PlaceId2? result)
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

        if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Place.SystemPlaceIds.Contains(s)))
            return false;

        result = new PlaceId2(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
