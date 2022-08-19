using ActualChat.Comparison;

namespace ActualChat.Users;

public record Author : IAuthorLike, IRequirementTarget
{
    private static IEqualityComparer<Author> EqualityComparer { get; } =
        VersionBasedEqualityComparer<Author, Symbol>.Instance;

    public static Author None { get; } = new();

    public Symbol Id { get; init; } = Symbol.Empty;
    public long Version { get; init; }
    public string Name { get; init; } = "";
    public string Picture { get; init; } = "";
    public bool IsAnonymous { get; init; }

    // This record relies on version-based equality
    public virtual bool Equals(Author? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
