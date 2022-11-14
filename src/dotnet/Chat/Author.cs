using ActualChat.Comparison;
using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public record Author : IHasId<AuthorId>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<Author> EqualityComparer { get; } =
        VersionBasedEqualityComparer<Author, AuthorId>.Instance;
    public static Requirement<Author> MustExist { get; } = Requirement.New(
        new(() => StandardError.Author.Unavailable()),
        (Author? a) => a is { Id.IsEmpty: false });

    public static Author None { get; } = new() { Avatar = Avatar.None };
    public static Author Loading { get; } = new() { Avatar = Avatar.Loading }; // Should differ by ref. from None

    [DataMember] public AuthorId Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol AvatarId { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Auto-populated
    [DataMember] public bool IsAnonymous { get; init; }
    [DataMember] public bool HasLeft { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;

    // This record relies on version-based equality
    public virtual bool Equals(Author? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
