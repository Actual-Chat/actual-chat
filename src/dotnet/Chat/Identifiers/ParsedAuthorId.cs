namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedAuthorId : IEquatable<ParsedAuthorId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => ChatId.IsValid;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedAuthorId(Symbol id) : this()
    {
        Id = id;
        if (Id.IsEmpty)
            return;

        var idValue = Id.Value;
        var chatIdLength = idValue.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return;

        var chatId = idValue[..chatIdLength];
        var tail = idValue[(chatIdLength + 1)..];
        if (!long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return;

        ChatId = chatId;
        LocalId = localId;
    }

    public ParsedAuthorId(Symbol chatId, long localId)
    {
        ChatId = chatId;
        LocalId = localId;
        Id = $"{chatId.Value}:{localId.ToString(CultureInfo.InvariantCulture)}";
    }

    private ParsedAuthorId(Symbol id, Symbol chatId, long localId)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public ParsedAuthorId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid author Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedAuthorId(Symbol source) => new(source);
    public static implicit operator ParsedAuthorId(string source) => new(source);
    public static implicit operator Symbol(ParsedAuthorId source) => source.Id;
    public static implicit operator string(ParsedAuthorId source) => source.Id;

    public void Deconstruct(out ParsedChatId chatId, out long localId)
    {
        chatId = ChatId;
        localId = LocalId;
    }

    // Equality

    public bool Equals(ParsedAuthorId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedAuthorId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedAuthorId left, ParsedAuthorId right) => left.Equals(right);
    public static bool operator !=(ParsedAuthorId left, ParsedAuthorId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedAuthorId result)
    {
        result = new ParsedAuthorId(value);
        return result.IsValid;
    }

    public static ParsedAuthorId Parse(string value)
        => new ParsedAuthorId(value).RequireValid();
}
