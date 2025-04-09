using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<PlaceChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PlaceChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PlaceChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class PlaceChatId2(string value, PlaceId2 placeId, string localChatId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<PlaceChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceChatId2>();
    private static readonly ILruCache<string, PlaceChatId2> Cache = CreateCache<PlaceChatId2>(256);

    public static readonly string IdPrefix = "s-";

    [IgnoreDataMember]
    public PlaceId2 PlaceId { get; } = placeId;
    [IgnoreDataMember]
    public string LocalChatId { get; } = localChatId;
    [IgnoreDataMember]
    public bool IsRoot => string.Equals(PlaceId.Value, LocalChatId, StringComparison.Ordinal);

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public ChatId2 AsChatId => field ??= ChatId2.Get(this);

    // Factories

    public static PlaceChatId2 New(PlaceId2 placeId)
    {
        var localChatId = ChatId2.IdGenerator.Next();
        return new(Format(placeId, localChatId), placeId, localChatId, AssumeValid.Option);
    }

    public static PlaceChatId2 NewRoot(PlaceId2 placeId)
    {
        var localChatId = placeId.Value;
        return new(Format(placeId, localChatId), placeId, localChatId, AssumeValid.Option);
    }

    // Equality

    public bool Equals(PlaceChatId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PlaceChatId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PlaceChatId2? left, PlaceChatId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PlaceChatId2? left, PlaceChatId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(PlaceId2 placeId, string localChatId)
        => $"{IdPrefix}{placeId}-{localChatId}";

    public static PlaceChatId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceChatId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out PlaceChatId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (!s.OrdinalStartsWith(IdPrefix))
            return false;

        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return false;

        if (!PlaceId2.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return false;
        if (!ChatId2.TryParse(tail[(placeIdLength + 1)..].ToString(), out var localChatId))
            return false;
        if (localChatId.Kind != ChatKind.Group)
            return false; // Both PlaceId and local ChatId must be there

        result = new PlaceChatId2(s, placeId, localChatId.Value, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
