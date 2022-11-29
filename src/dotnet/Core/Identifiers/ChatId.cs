namespace ActualChat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatId : ISymbolIdentifier<ChatId>
{
    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => Value.OrdinalStartsWith(PeerChatId.IdPrefix) ? ChatKind.Peer : ChatKind.Group;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatId(Symbol id) => this = Parse(id);
    public ChatId(string? id) => this = Parse(id);
    public ChatId(string? id, ParseOrDefaultOption _) => ParseOrDefault(id);

    public ChatId(Symbol id, SkipParseOption _)
        => Id = id;

    public bool IsGroupChatId()
        => Kind == ChatKind.Group;
    public bool IsPeerChatId(out PeerChatId peerChatId)
        => PeerChatId.TryParse(Value, out peerChatId);

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
        if (result.Kind == ChatKind.Peer && !result.IsPeerChatId(out _)) {
            // Invalid peer chat ID
            result = default;
            return false;
        }

        return true;
    }
}
