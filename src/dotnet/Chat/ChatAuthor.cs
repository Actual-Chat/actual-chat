using ActualChat.Comparison;
using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public record ChatAuthor : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<ChatAuthor> EqualityComparer { get; } =
        VersionBasedEqualityComparer<ChatAuthor, Symbol>.Instance;
    public static Requirement<ChatAuthor> MustExist { get; } = Requirement.New(
        new(() => StandardError.ChatAuthor.Unavailable()),
        (ChatAuthor? a) => !ReferenceEquals(a, null));

    [DataMember] public Symbol Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol ChatId { get; init; }
    [DataMember] public Symbol AvatarId { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Auto-populated
    [DataMember] public bool IsAnonymous { get; init; }
    [DataMember] public bool HasLeft { get; init; }

    // This record relies on version-based equality
    public virtual bool Equals(ChatAuthor? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
