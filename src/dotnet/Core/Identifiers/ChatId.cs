using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Generators;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ChatId : ISymbolIdentifier<ChatId>
{
    private static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public static ChatId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    private readonly UserId UserId1;
    private readonly UserId UserId2;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ChatKind Kind => UserId1.IsNone ? ChatKind.Group : ChatKind.Peer;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ChatId(Symbol id)
        => this = Parse(id);
    public ChatId(string? id)
        => this = Parse(id);
    public ChatId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public ChatId(Generate _)
        => this = new ChatId(IdGenerator.Next());

    public ChatId(Symbol id, UserId userId1, UserId userId2, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        UserId1 = userId1;
        UserId2 = userId2;
    }

    // Helpers

    public bool IsPeerChat(out PeerChatId peerChatId)
    {
        if (UserId1.IsNone) {
            peerChatId = default;
            return false;
        }
        peerChatId = new PeerChatId(Id, UserId1, UserId2, AssumeValid.Option);
        return true;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatId source) => source.Id;
    public static implicit operator string(ChatId source) => source.Id.Value;

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
        => TryParse(s, out var result) ? result : StandardError.Format<ChatId>(s).LogWarning(DefaultLog, None);

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

            result = new ChatId(peerChatId.Id, peerChatId.UserId1, peerChatId.UserId2, AssumeValid.Option);
        }
        else {
            if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Chat.SystemChatIds.Contains(s)))
                return false;

            // Group chat ID
            result = new ChatId(s, default, default, AssumeValid.Option);
        }
        return true;
    }
}
