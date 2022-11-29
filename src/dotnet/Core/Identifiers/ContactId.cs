namespace ActualChat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ContactId : ISymbolIdentifier<ContactId>
{
    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ContactId(Symbol id) => this = Parse(id);
    public ContactId(UserId ownerId, ChatId chatId) => Parse(Format(ownerId, chatId));
    public ContactId(UserId ownerId, ChatId chatId, ParseOrDefaultOption _) => ParseOrDefault(Format(ownerId, chatId));
    public ContactId(string id) => this = Parse(id);
    public ContactId(string id, ParseOrDefaultOption _) => ParseOrDefault(id);

    public ContactId(Symbol id, UserId ownerId, ChatId chatId, SkipParseOption _)
    {
        Id = id;
        OwnerId = ownerId;
        ChatId = chatId;
    }

    public ContactId(UserId ownerId, ChatId chatId, SkipParseOption _)
    {
        Id = Format(ownerId, chatId);
        OwnerId = ownerId;
        ChatId = chatId;
    }

    // Conversion

    private static string Format(UserId ownerId, ChatId chatId)
        => $"{ownerId} {chatId}";

    public override string ToString() => Value;
    public static implicit operator Symbol(ContactId source) => source.Id;
    public static implicit operator string(ContactId source) => source.Value;

    // Equality

    public bool Equals(ContactId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ContactId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ContactId left, ContactId right) => left.Equals(right);
    public static bool operator !=(ContactId left, ContactId right) => !left.Equals(right);

    // Parsing

    public static ContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId>();
    public static ContactId ParseOrDefault(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        var spaceIndex = s.IndexOf(' ');
        if (spaceIndex <= 0)
            return false;

        if (!UserId.TryParse(s[..spaceIndex], out var ownerId))
            return false;
        if (!ChatId.TryParse(s[(spaceIndex + 1)..], out var chatId))
            return false;

        result = new ContactId(s, ownerId, chatId, ParseOptions.Skip);
        return true;
    }
}
