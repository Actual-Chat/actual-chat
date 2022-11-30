using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatId>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatId : ISymbolIdentifier<ChatId>
{
    public static ChatId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    private readonly UserId UserId1;
    private readonly UserId UserId2;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => UserId1.IsNone ? ChatKind.Group : ChatKind.Peer;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatId(Symbol id)
        => this = Parse(id);
    public ChatId(string? id)
        => this = Parse(id);
    public ChatId(string? id, ParseOrNoneOption _)
        => this = ParseOrNone(id);

    public ChatId(Symbol id, UserId userId1, UserId userId2, SkipParseOption _)
    {
        Id = id;
        UserId1 = userId1;
        UserId2 = userId2;
    }

    public bool IsPeerChatId(out PeerChatId peerChatId)
    {
        if (UserId1.IsNone) {
            peerChatId = default;
            return false;
        }
        peerChatId = new PeerChatId(Id, UserId1, UserId2, ParseOptions.Skip);
        return true;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatId source) => source.Id;
    public static implicit operator string(ChatId source) => source.Value;

    // Equality

    public bool Equals(ChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
    public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);

    // Parsing

    public static ChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>();
    public static ChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty() || s.Length < 6)
            return false;
        if (!Alphabet.AlphaNumericDash.IsMatch(s))
            return false;

        if (s.OrdinalStartsWith(PeerChatId.IdPrefix)) {
            // Peer chat ID
            if (!PeerChatId.TryParse(s, out var peerChatId))
                return false;

            result = new ChatId(peerChatId.Id, peerChatId.UserId1, peerChatId.UserId2, ParseOptions.Skip);
            return true;
        }

        // Group chat ID
        result = new ChatId(s, default, default, ParseOptions.Skip);
        return true;
    }
}
