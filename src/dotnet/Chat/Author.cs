using ActualChat.Comparison;
using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public record Author(
    [property: DataMember] AuthorId Id,
    [property: DataMember] long Version = 0
    ): IHasId<AuthorId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Author, AuthorId> EqualityComparer { get; } = new();

    public static Author None { get; } = AuthorFull.None.ToAuthor();
    public static Author Loading { get; } = AuthorFull.Loading.ToAuthor();

    public static Requirement<Author> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Author>()),
        (Author? a) => a is { Id.IsNone: false });

    [DataMember] public Symbol AvatarId { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Auto-populated
    [DataMember] public bool IsAnonymous { get; init; }
    [DataMember] public bool HasLeft { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;

    // This record relies on version-based equality
    public virtual bool Equals(Author? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
