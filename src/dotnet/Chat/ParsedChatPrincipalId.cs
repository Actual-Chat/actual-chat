using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatPrincipalId : IEquatable<ParsedChatPrincipalId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatPrincipalKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedChatAuthorId AuthorId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => UserId.IsValid || AuthorId.IsValid;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedChatPrincipalId(Symbol id)
    {
        Id = id;
        var hasDivider = Id.Value.OrdinalContains(':');
        Kind = hasDivider ? ChatPrincipalKind.Author : ChatPrincipalKind.User;
        UserId = Kind == ChatPrincipalKind.User ? Id : default;
        AuthorId = Kind == ChatPrincipalKind.Author ? Id : default;
    }

    public ParsedChatPrincipalId AssertValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat principal Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedChatPrincipalId(Symbol source) => new(source);
    public static implicit operator ParsedChatPrincipalId(string source) => new(source);
    public static implicit operator Symbol(ParsedChatPrincipalId source) => source.Id;
    public static implicit operator string(ParsedChatPrincipalId source) => source.Id;

    // Equality

    public bool Equals(ParsedChatPrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedChatPrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedChatPrincipalId left, ParsedChatPrincipalId right) => left.Equals(right);
    public static bool operator !=(ParsedChatPrincipalId left, ParsedChatPrincipalId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedChatPrincipalId result)
    {
        result = new ParsedChatPrincipalId(value);
        return result.IsValid;
    }

    public static ParsedChatPrincipalId Parse(string value)
        => new ParsedChatPrincipalId(value).AssertValid();
}
