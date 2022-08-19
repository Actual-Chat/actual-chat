using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Users;

public record UserAvatar : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<UserAvatar> EqualityComparer { get; } =
        VersionBasedEqualityComparer<UserAvatar, Symbol>.Instance;

    public Symbol Id { get; init; } = Symbol.Empty;
    public long Version { get; init; }
    public Symbol UserId { get; init; }
    public string Name { get; init; } = "";
    public string Picture { get; init; } = "";
    public string Bio { get; init; } = "";

    // This record relies on version-based equality
    public virtual bool Equals(UserAvatar? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
