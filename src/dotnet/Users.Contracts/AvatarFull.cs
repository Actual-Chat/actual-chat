using ActualChat.Comparison;

namespace ActualChat.Users;

public sealed record AvatarFull : Avatar
{
    private static IEqualityComparer<AvatarFull> EqualityComparer { get; } =
        VersionBasedEqualityComparer<AvatarFull, Symbol>.Instance;
    public static new Requirement<AvatarFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Avatar>()),
        (AvatarFull? a) => a is { Id.IsEmpty : false });

    public static new AvatarFull None { get; } = new();
    public static new AvatarFull Loading { get; } = new(); // Should differ by ref. from None

    [DataMember] public Symbol ChatPrincipalId { get; init; }

    // Helpers

    public AvatarFull WithMissingPropertiesFrom(AvatarFull? other)
        => (AvatarFull) base.WithMissingPropertiesFrom(other);
    public new AvatarFull WithMissingPropertiesFrom(Avatar? other)
        => (AvatarFull) base.WithMissingPropertiesFrom(other);

    // This record relies on version-based equality
    public bool Equals(AvatarFull? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
