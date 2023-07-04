using ActualChat.Comparison;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Avatar(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Avatar, Symbol> EqualityComparer { get; } = new();

    public const string GuestName = "Guest";
    public static Avatar None { get; } = new(Symbol.Empty, 0);
    public static Avatar Loading { get; } = new(Symbol.Empty, -1); // Should differ by ref. from None

    public static Requirement<Avatar> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (Avatar? a) => a is { Id.IsEmpty : false });

    [DataMember, MemoryPackOrder(2)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string Picture { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public MediaId MediaId { get; init; }
    [IgnoreDataMember, MemoryPackIgnore] public UserPicture UserPicture => new (Media?.ContentId, Picture);
    [DataMember, MemoryPackOrder(5)] public string Bio { get; init; } = "";

    // Populated only on reads
    [DataMember, MemoryPackOrder(6)] public Media.Media? Media { get; init; }

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
