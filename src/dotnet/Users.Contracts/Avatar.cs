using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
public record Avatar : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<Avatar> EqualityComparer { get; } =
        VersionBasedEqualityComparer<Avatar, Symbol>.Instance;

    [DataMember] public Symbol Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public string Name { get; init; } = "";
    [DataMember] public string Picture { get; init; } = "";
    [DataMember] public string Bio { get; init; } = "";

    // This record relies on version-based equality
    public virtual bool Equals(Avatar? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
