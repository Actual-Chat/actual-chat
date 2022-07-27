namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatAuthorId : IEquatable<ParsedChatAuthorId>, IHasId<Symbol>
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
    public ParsedChatAuthorId(Symbol id) : this()
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

    public ParsedChatAuthorId(Symbol chatId, long localId)
    {
        ChatId = chatId;
        LocalId = localId;
        Id = $"{chatId.Value}:{localId.ToString(CultureInfo.InvariantCulture)}";
    }

    private ParsedChatAuthorId(Symbol id, Symbol chatId, long localId)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public ParsedChatAuthorId AssertValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat author Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedChatAuthorId(Symbol source) => new(source);
    public static implicit operator ParsedChatAuthorId(string source) => new(source);
    public static implicit operator Symbol(ParsedChatAuthorId source) => source.Id;
    public static implicit operator string(ParsedChatAuthorId source) => source.Id;

    public void Deconstruct(out ParsedChatId chatId, out long localId)
    {
        chatId = ChatId;
        localId = LocalId;
    }

    // Equality

    public bool Equals(ParsedChatAuthorId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedChatAuthorId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedChatAuthorId left, ParsedChatAuthorId right) => left.Equals(right);
    public static bool operator !=(ParsedChatAuthorId left, ParsedChatAuthorId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedChatAuthorId result)
    {
        result = new ParsedChatAuthorId(value);
        return result.IsValid;
    }

    public static ParsedChatAuthorId Parse(string value)
        => new ParsedChatAuthorId(value).AssertValid();
}
