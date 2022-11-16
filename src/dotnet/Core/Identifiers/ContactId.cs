namespace ActualChat;

#pragma warning disable MA0011

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ContactId : IEquatable<ContactId>, IRequirementTarget, ICanBeEmpty
{
    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ContactKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; } // Must be set even for user contacts
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    private UserId UserId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ContactId(Symbol id) => this = Parse(id);
    public ContactId(string id) => this = Parse(id);
    public ContactId(string id, ParseOrDefaultTag _) => ParseOrDefault(id);

    public ContactId(Symbol id, UserId ownerId, UserId userId, SkipParseTag _)
    {
        Id = id;
        Kind = ContactKind.User;
        OwnerId = ownerId;
        UserId = userId;
        ChatId = new ChatId(OwnerId, userId, ActualChat.Parse.None);
    }

    public ContactId(Symbol id, UserId ownerId, ChatId chatId, SkipParseTag _)
    {
        Id = id;
        Kind = ContactKind.Chat;
        OwnerId = ownerId;
        UserId = default;
        ChatId = chatId;
    }

    public ContactId(UserId ownerId, UserId userId, SkipParseTag _)
    {
        Id = $"{ownerId} u:{userId}";
        Kind = ContactKind.User;
        OwnerId = ownerId;
        UserId = userId;
        ChatId = new ChatId(OwnerId, userId, ActualChat.Parse.None);
    }

    public ContactId(UserId ownerId, ChatId chatId, SkipParseTag _)
    {
        Id = $"{ownerId} c:{chatId}";
        Kind = ContactKind.Chat;
        OwnerId = ownerId;
        UserId = default;
        ChatId = chatId;
    }

    public bool IsUserContact(out UserId userId)
    {
        userId = UserId;
        return Kind == ContactKind.User;
    }

    public bool IsChatContact()
        => Kind == ContactKind.Chat;

    // Conversion

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

    public static ContactId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId>();
    public static ContactId ParseOrDefault(string s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        var spaceIndex = s.IndexOf(' ');
        if (spaceIndex <= 0 || spaceIndex + 3 >= s.Length)
            return false;
        if (s[spaceIndex + 2] != ':')
            return false;
        if (!UserId.TryParse(s[..spaceIndex], out var ownerId))
            return false;

        switch (s[spaceIndex + 1]) {
        case 'u':
            if (!UserId.TryParse(s[(spaceIndex + 3)..], out var userId))
                return false;
            result = new ContactId(s, ownerId, userId, ActualChat.Parse.None);
            return true;
        case 'c':
            if (!ChatId.TryParse(s[(spaceIndex + 3)..], out var chatId))
                return false;
            result = new ContactId(s, ownerId, chatId, ActualChat.Parse.None);
            return true;
        }
        return false;
    }
}
