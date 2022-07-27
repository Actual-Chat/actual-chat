namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatRoleId : IEquatable<ParsedChatRoleId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid { get; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedChatRoleId(Symbol id) : this()
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
        IsValid = ChatId.IsValid;
    }

    public ParsedChatRoleId(ParsedChatId chatId, long localId)
    {
        ChatId = chatId;
        LocalId = localId;
        Id = $"{chatId.Id}:{localId.ToString(CultureInfo.InvariantCulture)}";
        IsValid = ChatId.IsValid;
    }

    private ParsedChatRoleId(Symbol id, ParsedChatId chatId, long localId)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
        IsValid = ChatId.IsValid;
    }

    public ParsedChatRoleId AssertValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat role Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedChatRoleId(Symbol source) => new(source);
    public static implicit operator ParsedChatRoleId(string source) => new(source);
    public static implicit operator Symbol(ParsedChatRoleId source) => source.Id;
    public static implicit operator string(ParsedChatRoleId source) => source.Id;

    // Equality

    public bool Equals(ParsedChatRoleId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedChatRoleId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedChatRoleId left, ParsedChatRoleId right) => left.Equals(right);
    public static bool operator !=(ParsedChatRoleId left, ParsedChatRoleId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedChatRoleId result)
    {
        result = new ParsedChatRoleId(value);
        return result.IsValid;
    }

    public static ParsedChatRoleId Parse(string value)
        => new ParsedChatRoleId(value).AssertValid();
}
