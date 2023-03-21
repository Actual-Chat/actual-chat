using Stl.Fusion.Blazor;

namespace ActualChat.Users;

[DataContract]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record AvatarFull(Symbol Id, long Version = 0) : Avatar(Id, Version)
{
    public static new Requirement<AvatarFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (AvatarFull? a) => a is { Id.IsEmpty : false });

    public static new AvatarFull None { get; } = new(Symbol.Empty, 0);
    public static new AvatarFull Loading { get; } = new(Symbol.Empty, -1); // Should differ by ref. from None

    [DataMember] public UserId UserId { get; init; }

    public AvatarFull() : this(Symbol.Empty) { }

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

    // This record relies on version-based equality
    public bool Equals(AvatarFull? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
