using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedPrincipalId : IEquatable<ParsedPrincipalId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public PrincipalKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedAuthorId AuthorId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => Kind is not PrincipalKind.Invalid;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValidOrEmpty => IsValid || Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedPrincipalId(Symbol id)
    {
        Id = id;
        AuthorId = new ParsedAuthorId(id);
        if (AuthorId.IsValid) {
            Kind = PrincipalKind.Author;
            UserId = default;
            return;
        }

        UserId = new ParsedUserId(id);
        if (UserId.IsValid) {
            Kind = PrincipalKind.User;
            AuthorId = default;
            return;
        }

        AuthorId = default;
        UserId = default;
        Kind = PrincipalKind.Invalid;
    }

    public ParsedPrincipalId(Symbol authorId, Symbol userId)
    {
        if (!authorId.IsEmpty) {
            if (!userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");

            Id = authorId;
            AuthorId = new ParsedAuthorId(authorId);
            UserId = default;
            Kind = AuthorId.IsValid ? PrincipalKind.Author : PrincipalKind.Invalid;
        }
        else {
            if (userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");

            Id = userId;
            UserId = new ParsedUserId(userId);
            AuthorId = default;
            Kind = UserId.IsValid ? PrincipalKind.User : PrincipalKind.Invalid;
        }
    }

    public void Deconstruct(out ParsedAuthorId authorId, out ParsedUserId userId)
    {
        authorId = AuthorId;
        userId = UserId;
    }

    public ParsedPrincipalId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid principal Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedPrincipalId(Symbol source) => new(source);
    public static implicit operator ParsedPrincipalId(string source) => new(source);
    public static implicit operator Symbol(ParsedPrincipalId source) => source.Id;
    public static implicit operator string(ParsedPrincipalId source) => source.Id;

    // Equality

    public bool Equals(ParsedPrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedPrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedPrincipalId left, ParsedPrincipalId right) => left.Equals(right);
    public static bool operator !=(ParsedPrincipalId left, ParsedPrincipalId right) => !left.Equals(right);
}
