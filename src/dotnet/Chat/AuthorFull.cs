using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record AuthorFull(AuthorId Id, long Version = 0) : Author(Id, Version)
{
    public static new Requirement<AuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.Author.Unavailable()),
        (AuthorFull? a) => a is { Id.IsEmpty: false });

    public static new AuthorFull None { get; } = new(default, 0) { Avatar = Avatar.None };
    public static new AuthorFull Loading { get; } = new(default, 1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    [DataMember] public UserId UserId { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;

    // This record relies on version-based equality
    public bool Equals(AuthorFull? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
