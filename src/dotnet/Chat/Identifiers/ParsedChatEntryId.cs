namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatEntryId : IEquatable<ParsedChatEntryId>, IHasId<Symbol>
{
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryType EntryType { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long EntryId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => ChatId.IsValid && EntryId >= 0 && Enum.IsDefined(EntryType);

    public ParsedChatEntryId(Symbol id)
    {
        Id = id;
        ChatId = "";
        EntryType = (ChatEntryType) (-1);
        EntryId = -1;
        if (id.IsEmpty)
            return;

        var idParts = Id.Value.Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
        if (idParts.Length != 3)
            return;

        ChatId = idParts[0];
        EntryType = Enum.TryParse<ChatEntryType>(idParts[1], out var entryType)
            ? entryType
            : (ChatEntryType)(-1);
        EntryId = long.TryParse(idParts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var entryId)
            ? entryId
            : -1;
    }

    public ParsedChatEntryId(string chatId, ChatEntryType entryType, long entryId)
    {
        ChatId = chatId;
        EntryType = entryType;
        EntryId = entryId;
        Id = $"{chatId}:{entryType:D}:{entryId.ToString(CultureInfo.InvariantCulture)}";
    }

    public ParsedChatEntryId Invalid()
        => this;

    public ParsedChatEntryId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedChatEntryId(Symbol source) => new(source);
    public static implicit operator ParsedChatEntryId(string source) => new(source);
    public static implicit operator Symbol(ParsedChatEntryId source) => source.Id;
    public static implicit operator string(ParsedChatEntryId source) => source.Id;

    // Equality

    public bool Equals(ParsedChatEntryId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ParsedChatEntryId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedChatEntryId left, ParsedChatEntryId right) => left.Equals(right);
    public static bool operator !=(ParsedChatEntryId left, ParsedChatEntryId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedChatEntryId result)
    {
        result = new ParsedChatEntryId(value);
        return result.IsValid;
    }

    public static ParsedChatEntryId Parse(string value)
        => new ParsedChatEntryId(value).RequireValid();
}
