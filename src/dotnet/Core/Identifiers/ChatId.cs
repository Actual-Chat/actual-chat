using System.ComponentModel;
using MemoryPack;
using ActualLab.Generators;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ChatId : ISymbolIdentifier<ChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatId>();
    internal static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public static ChatId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PeerChatId PeerChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceChatId PlaceChatId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatKind Kind => !PlaceChatId.IsNone
        ? ChatKind.Place
        : PeerChatId.IsNone
            ? ChatKind.Group
            : ChatKind.Peer;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsPlaceChat => !PlaceChatId.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsPlaceRootChat => IsPlaceChat && PlaceChatId.IsRoot;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceId PlaceId => PlaceChatId.PlaceId;

    // Factories

    public static ChatId Group(Symbol chatId)
        => new(chatId, default, default, AssumeValid.Option);
    public static ChatId Peer(PeerChatId peerChatId)
        => new(peerChatId.Id, peerChatId, default, AssumeValid.Option);
    public static ChatId Place(PlaceChatId placeChatId)
        => new(placeChatId.Id, default, placeChatId, AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ChatId(Symbol id)
        => this = Parse(id);
    public ChatId(string? id)
        => this = Parse(id);
    public ChatId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public ChatId(Generate _)
        => this = new ChatId(IdGenerator.Next());

    private ChatId(Symbol id, PeerChatId peerChatId, PlaceChatId placeChatId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        PeerChatId = peerChatId;
        PlaceChatId = placeChatId;
    }

    // Helpers

    public bool IsPeerChat(out PeerChatId peerChatId)
    {
        peerChatId = PeerChatId;
        return !peerChatId.IsNone;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatId source) => source.Id;
    public static implicit operator string(ChatId source) => source.Id.Value;
    public static implicit operator ChatId(PeerChatId source) => new(source.Id, source, PlaceChatId.None, AssumeValid.Option);
    public static implicit operator ChatId(PlaceChatId source) => new(source.Id, PeerChatId.None, source, AssumeValid.Option);
    public static explicit operator ChatId(string source) => new(source);

    // Equality

    public bool Equals(ChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
    public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);

    // Parsing

    public static ChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>(s);
    public static ChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6)
            return false;

        if (s.OrdinalStartsWith(PeerChatId.IdPrefix)) {
            // Peer chat ID
            if (!PeerChatId.TryParse(s, out var peerChatId))
                return false;

            result = new ChatId(peerChatId.Id, peerChatId, PlaceChatId.None, AssumeValid.Option);
        }
        else if (s.OrdinalStartsWith(PlaceChatId.IdPrefix)) {
            // Place chat ID
            if (!PlaceChatId.TryParse(s, out var placeChatId))
                return false;

            result = new ChatId(placeChatId.Id, PeerChatId.None, placeChatId, AssumeValid.Option);
        }
        else {
            if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Chat.SystemChatIds.Contains(s)))
                return false;

            // Group chat ID
            result = new ChatId(s, PeerChatId.None, PlaceChatId.None, AssumeValid.Option);
        }
        return true;
    }
}
