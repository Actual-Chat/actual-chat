using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record AvatarFull(
    [property: DataMember, MemoryPackOrder(7)] UserId UserId,
    Symbol Id = default,
    long Version = 0) : Avatar(Id, Version)
{
    public static new readonly Requirement<AvatarFull> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (AvatarFull? a) => a is { Id.IsEmpty : false });

    public static new readonly AvatarFull None = new( UserId.None,Symbol.Empty, 0);
    public static new readonly AvatarFull Loading = new(UserId.None, Symbol.Empty, -1); // Should differ by ref. from None

    [DataMember, MemoryPackOrder(8)] public bool IsAnonymous { get; init; }

    // Helpers

    public Avatar ToAvatar() => new(Id, Version) {
        Name = Name,
        Bio = Bio,
        MediaId = MediaId,
    };

    public AvatarFull WithMissingPropertiesFrom(AvatarFull? other)
        => (AvatarFull) base.WithMissingPropertiesFrom(other);
    public new AvatarFull WithMissingPropertiesFrom(Avatar? other)
        => (AvatarFull) base.WithMissingPropertiesFrom(other);
    public new AvatarFull WithPicture(Picture? picture)
        => (AvatarFull) base.WithPicture(picture);

    // This record relies on referential equality
    public bool Equals(AvatarFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
