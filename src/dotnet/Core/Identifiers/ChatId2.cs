using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatId2(string value, PeerChatId2? peerChatId, PlaceChatId2? placeChatId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<ChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatId2>();
    internal static readonly ILruCache<string, ChatId2> Cache = CreateCache<ChatId2>(256);
    internal static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    [IgnoreDataMember]
    public PeerChatId2? PeerChatId { get; } = peerChatId;
    [IgnoreDataMember]
    public PlaceChatId2? PlaceChatId { get; } = placeChatId;

    [IgnoreDataMember]
    public ChatKind Kind { get; } = placeChatId != null
        ? ChatKind.Place
        : peerChatId == null
            ? ChatKind.Group
            : ChatKind.Peer;

    [IgnoreDataMember]
    public bool IsPlaceChat => PlaceChatId != null;
    [IgnoreDataMember]
    public bool IsPlaceRootChat => PlaceChatId != null && PlaceChatId.IsRoot;
    [IgnoreDataMember]
    public PlaceId2? PlaceId => PlaceChatId?.PlaceId;

    // Factories

    public static ChatId2 NewGroup()
        => new(IdGenerator.Next(), null, null, AssumeValid.Option);

    public static ChatId2 NewGroup(string groupChatId)
        => new(groupChatId, null, null, AssumeValid.Option);

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
            // Peer chat ID
            if (!PeerChatId2.TryParse(s, out var peerChatId))
                return false;

            result = new ChatId2(peerChatId.Value, peerChatId, null, AssumeValid.Option);
        }
        else if (s.OrdinalStartsWith(PlaceChatId2.IdPrefix)) {
            // Place chat ID
            if (!PlaceChatId2.TryParse(s, out var placeChatId))
                return false;

            result = new ChatId2(placeChatId.Value, null, placeChatId, AssumeValid.Option);
        }
        else {
            if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Chat.SystemChatIds.Contains(s)))
                return false;

            // Group chat ID
            result = new ChatId2(s, null, null, AssumeValid.Option);
        }

        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Get helpers

    internal static ChatId2 Get(PeerChatId2 peerChatId)
        => Cache.GetOrCreate(peerChatId.Value,
            static peerChatId => new(peerChatId.Value, peerChatId, null, AssumeValid.Option),
            peerChatId);

    internal static ChatId2 Get(PlaceChatId2 placeChatId)
        => Cache.GetOrCreate(placeChatId.Value,
            static placeChatId => new(placeChatId.Value, null, placeChatId, AssumeValid.Option),
            placeChatId);
}
