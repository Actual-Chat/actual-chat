namespace ActualChat;

#pragma warning disable MA0011

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct AuthorId : IEquatable<AuthorId>, IParsable<AuthorId>, IRequirementTarget, ICanBeEmpty
{
    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public AuthorId(Symbol id) => Parse(id);
    public AuthorId(string id) => Parse(id);

    public AuthorId(Symbol id, ChatId chatId, long localId, SkipValidation _)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public AuthorId(ChatId chatId, long localId, SkipValidation _)
    {
        Id = $"{chatId.Value}:{localId.ToString(CultureInfo.InvariantCulture)}";
        ChatId = chatId;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(AuthorId source) => source.Id;
    public static implicit operator string(AuthorId source) => source.Value;

    // Equality

    public bool Equals(AuthorId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is AuthorId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(AuthorId left, AuthorId right) => left.Equals(right);
    public static bool operator !=(AuthorId left, AuthorId right) => !left.Equals(right);

    // Parsing

    public static AuthorId Parse(string s, IFormatProvider? provider)
        => Parse(s);
    public static AuthorId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AuthorId>();

    public static bool TryParse(string? s, IFormatProvider? provider, out AuthorId result)
        => TryParse(s, out result);
    public static bool TryParse(string? s, out AuthorId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var tail = s[(chatIdLength + 1)..];
        if (!long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return false;

        result = new AuthorId(s, chatId, localId, SkipValidation.Instance);
        return true;
    }
}
