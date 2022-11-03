using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
public record Avatar : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<Avatar> EqualityComparer { get; } =
        VersionBasedEqualityComparer<Avatar, Symbol>.Instance;
    public static Requirement<Avatar> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (Avatar? a) => a is { Id.IsEmpty : false });

    public static Avatar None { get; } = new();
    public static Avatar Loading { get; } = new(); // Should differ by ref. from None

    [DataMember] public Symbol Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public string Name { get; init; } = "";
    [DataMember] public string Picture { get; init; } = "";
    [DataMember] public string Bio { get; init; } = "";

    // Helpers

    public Avatar WithMissingPropertiesFrom(Avatar? other)
    {
        if (other == null)
            return this;

        var avatar = this;
        if (avatar.Name.IsNullOrEmpty())
            avatar = avatar with { Name = other.Name };
        if (avatar.Bio.IsNullOrEmpty())
            avatar = avatar with { Bio = other.Bio };
        if (avatar.Picture.IsNullOrEmpty())
            avatar = avatar with { Picture = other.Picture };
        return avatar;
    }

    // This record relies on version-based equality
    public virtual bool Equals(Avatar? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
