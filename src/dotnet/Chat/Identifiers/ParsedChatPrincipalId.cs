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
    public ParsedAuthorId AuthorId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => Kind is not ChatPrincipalKind.Invalid;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValidOrEmpty => IsValid || Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedChatPrincipalId(Symbol id)
    {
        Id = id;
        AuthorId = new ParsedAuthorId(id);
        if (AuthorId.IsValid) {
            Kind = ChatPrincipalKind.Author;
            UserId = default;
            return;
        }

        UserId = new ParsedUserId(id);
        if (UserId.IsValid) {
            Kind = ChatPrincipalKind.User;
            AuthorId = default;
            return;
        }

        AuthorId = default;
        UserId = default;
        Kind = ChatPrincipalKind.Invalid;
    }

    public ParsedChatPrincipalId(Symbol authorId, Symbol userId)
    {
        if (!authorId.IsEmpty) {
            if (!userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");

            Id = authorId;
            AuthorId = new ParsedAuthorId(authorId);
            UserId = default;
            Kind = AuthorId.IsValid ? ChatPrincipalKind.Author : ChatPrincipalKind.Invalid;
        }
        else {
            if (userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");

            Id = userId;
            UserId = new ParsedUserId(userId);
            AuthorId = default;
            Kind = UserId.IsValid ? ChatPrincipalKind.User : ChatPrincipalKind.Invalid;
        }
    }

    public void Deconstruct(out ParsedAuthorId authorId, out ParsedUserId userId)
    {
        authorId = AuthorId;
        userId = UserId;
    }

    public ParsedChatPrincipalId RequireValid()
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
}
