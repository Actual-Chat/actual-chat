using ActualLab.Fusion.Blazor;

namespace ActualChat.Users;

/// <summary>
/// Extended <see cref="Avatar"/> with user association and anonymity flag.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
[method: MemoryPackConstructor]
public sealed partial record AvatarFull(
    [property: DataMember, MemoryPackOrder(7), Key(10)] UserId UserId,
    Symbol Id = default,
    long Version = 0) : Avatar(Id, Version)
{
    public static new readonly Requirement<AvatarFull> MustExist = Requirement.New(
        (AvatarFull? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Avatar>()));

    [DataMember, MemoryPackOrder(8), Key(11)] public bool IsAnonymous { get; init; }

    internal AvatarFull() : this(default!) { }

    // Helpers

    public Avatar ToAvatar() => new(Id, Version) {
        Name = Name,
        Bio = Bio,
        MediaId = MediaId,
        Media = Media,
        AvatarKey = AvatarKey,
        PictureUrl = PictureUrl,
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
