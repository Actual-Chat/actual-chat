using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ChatId2 : StringIdentifier, IStringIdentifier<ChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatId2>();
    private static readonly ILruCache<string, ChatId2> Cache = CreateCache<ChatId2>(512);

    public static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    [IgnoreDataMember]
    public ChatKind Kind { get; }

    // Factories and constructors

    protected ChatId2(string value, ChatKind kind) : base(value)
        => Kind = kind;

    // Equality

    public bool Equals(ChatId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ChatId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatId2? left, ChatId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatId2? left, ChatId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static ChatId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length < 6)
            return false;

        if (s.OrdinalStartsWith(PeerChatId2.IdPrefix)) {
            result = TryParsePeerChatId(s);
            return result != null;
        }
        if (s.OrdinalStartsWith(PlaceChatId2.IdPrefix)) {
            result = TryParsePlaceChatId(s);
            return result != null;
        }
        result = TryParseGroupChatId(s);
        if (result == null)
            return false;

        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Private methods

    private static PeerChatId2? TryParsePeerChatId(string s)
    {
        var tail = s.AsSpan(2);
        var userId1Length = tail.IndexOf('-');
        if (userId1Length < 0)
            return null;

        if (!UserId2.TryParse(tail[..userId1Length].ToString(), out var userId1))
            return null;
        if (!UserId2.TryParse(tail[(userId1Length + 1)..].ToString(), out var userId2))
            return null;
        if (string.CompareOrdinal(userId1.Value, userId2.Value) >= 0)
            return null; // Wrong sort order or they are the same

        return new PeerChatId2(s, userId1, userId2);
    }

    private static PlaceChatId2? TryParsePlaceChatId(string s)
    {
        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return null;

        if (!PlaceId2.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return null;
        if (!TryParse(tail[(placeIdLength + 1)..].ToString(), out var localChatId))
            return null;
        if (localChatId.Kind != ChatKind.Group)
            return null; // Both PlaceId and local ChatId must be there

        return new PlaceChatId2(s, placeId, localChatId.Value);
    }

    private static GroupChatId? TryParseGroupChatId(string s)
    {
        if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Chat.SystemChatIds.Contains(s)))
            return null;

        return new GroupChatId(s);
    }
}
