using ActualChat.Users;

namespace ActualChat.Chat;

#pragma warning disable MA0011

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct PrincipalId : IEquatable<PrincipalId>, IParsable<PrincipalId>, IRequirementTarget, ICanBeEmpty
{
    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public PrincipalKind Kind { get; }
    private AuthorId AuthorId { get; }
    private UserId UserId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public PrincipalId(Symbol id) => this = Parse(id);
    public PrincipalId(string id) => this = Parse(id);

    public PrincipalId(AuthorId authorId, SkipValidation _)
    {
        Id = authorId.Id;
        Kind = PrincipalKind.Author;
        AuthorId = authorId;
        UserId = default;
    }

    public PrincipalId(UserId userId, SkipValidation _)
    {
        Id = userId.Id;
        Kind = PrincipalKind.Author;
        AuthorId = default;
        UserId = userId;
    }

    public bool IsAuthor(out AuthorId authorId)
    {
        authorId = AuthorId;
        return Kind == PrincipalKind.Author;
    }

    public bool IsUser(out UserId userId)
    {
        userId = UserId;
        return Kind == PrincipalKind.User;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(PrincipalId source) => source.Id;
    public static implicit operator string(PrincipalId source) => source.Value;

    // Equality

    public bool Equals(PrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is PrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PrincipalId left, PrincipalId right) => left.Equals(right);
    public static bool operator !=(PrincipalId left, PrincipalId right) => !left.Equals(right);

    // Parsing

    public static PrincipalId Parse(string s, IFormatProvider? provider)
        => Parse(s);
    public static PrincipalId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PrincipalId>();

    public static bool TryParse(string? s, IFormatProvider? provider, out PrincipalId result)
        => TryParse(s, out result);
    public static bool TryParse(string? s, out PrincipalId result)
    {
        if (UserId.TryParse(s, out var userId)) {
            result = new PrincipalId(userId, SkipValidation.Instance);
            return true;
        }
        if (AuthorId.TryParse(s, out var authorId)) {
            result = new PrincipalId(authorId, SkipValidation.Instance);
            return true;
        }
        result = default;
        return false;
    }
}
