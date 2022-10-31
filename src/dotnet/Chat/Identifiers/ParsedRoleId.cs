namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedRoleId : IEquatable<ParsedRoleId>, IHasId<Symbol>
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
    public ParsedRoleId(Symbol id) : this()
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

    public ParsedRoleId(ParsedChatId chatId, long localId)
    {
        ChatId = chatId;
        LocalId = localId;
        Id = $"{chatId.Id}:{localId.ToString(CultureInfo.InvariantCulture)}";
        IsValid = ChatId.IsValid;
    }

    private ParsedRoleId(Symbol id, ParsedChatId chatId, long localId)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
        IsValid = ChatId.IsValid;
    }

    public ParsedRoleId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid role Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedRoleId(Symbol source) => new(source);
    public static implicit operator ParsedRoleId(string source) => new(source);
    public static implicit operator Symbol(ParsedRoleId source) => source.Id;
    public static implicit operator string(ParsedRoleId source) => source.Id;

    // Equality

    public bool Equals(ParsedRoleId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedRoleId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedRoleId left, ParsedRoleId right) => left.Equals(right);
    public static bool operator !=(ParsedRoleId left, ParsedRoleId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedRoleId result)
    {
        result = new ParsedRoleId(value);
        return result.IsValid;
    }

    public static ParsedRoleId Parse(string value)
        => new ParsedRoleId(value).RequireValid();
}
