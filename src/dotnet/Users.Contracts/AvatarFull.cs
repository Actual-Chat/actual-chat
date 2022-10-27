using ActualChat.Comparison;

namespace ActualChat.Users;

public sealed record AvatarFull : Avatar
{
    private static IEqualityComparer<AvatarFull> EqualityComparer { get; } =
        VersionBasedEqualityComparer<AvatarFull, Symbol>.Instance;

    [DataMember] public Symbol ChatPrincipalId { get; init; }

    // This record relies on version-based equality
    public bool Equals(AvatarFull? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
