namespace ActualChat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatId : ISymbolIdentifier<ChatId>
{
    public static readonly string PeerChatIdPrefix = "p-";

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => Value.OrdinalStartsWith(PeerChatIdPrefix) ? ChatKind.Peer : ChatKind.Group;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatId(Symbol id) => this = Parse(id);
    public ChatId(string? id) => this = Parse(id);
    public ChatId(string? id, ParseOrDefaultOption _) => ParseOrDefault(id);

    public ChatId(Symbol id, SkipParseOption _)
        => Id = id;

    public bool IsGroupChatId()
        => Kind == ChatKind.Group;

    public bool IsPeerChatId(out (UserId UserId1, UserId UserId2) userIds)
        => PeerChatId.TryParse(Value, out userIds);
    public bool IsPeerChatId(out UserId userId1, out UserId userId2)
        => PeerChatId.TryParse(Value, out userId1, out userId2);

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
    public static ChatId ParseOrDefault(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty() || s.Length < 6)
            return false;
        if (!Alphabet.AlphaNumericDash.IsMatch(s))
            return false;

        result = new ChatId(s, ParseOptions.Skip);
        return true;
    }
}
