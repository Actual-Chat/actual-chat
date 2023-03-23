using ActualChat.Comparison;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
[ParameterComparer(typeof(ByRefParameterComparer))]
public record Avatar(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Avatar, Symbol> EqualityComparer { get; } = new();

    public static Avatar None { get; } = new(Symbol.Empty, 0);
    public static Avatar Loading { get; } = new(Symbol.Empty, -1); // Should differ by ref. from None

    public static Requirement<Avatar> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (Avatar? a) => a is { Id.IsEmpty : false });

    [DataMember] public string Name { get; init; } = "";
    [DataMember] public string Picture { get; init; } = "";
    [DataMember] public MediaId MediaId { get; init; } = MediaId.None;
    [DataMember] public string Bio { get; init; } = "";

    // Populated only on reads
    [DataMember] public Media.Media? Media { get; init; }

    public Avatar() : this(Symbol.Empty) { }

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
        if (avatar.MediaId.IsNone)
            avatar = avatar with { MediaId = other.MediaId };
        if (avatar.Picture.IsNullOrEmpty())
            avatar = avatar with { Picture = other.Picture };
        return avatar;
    }

    // This record relies on version-based equality
    public virtual bool Equals(Avatar? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
