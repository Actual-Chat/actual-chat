using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record AuthorFull(AuthorId Id, long Version = 0) : Author(Id, Version)
{
    public static new Requirement<AuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Author>()),
        (AuthorFull? a) => a is { Id.IsNone: false });

    public static new AuthorFull None { get; } = new() { Avatar = Avatar.None };
    public static new AuthorFull Loading { get; } = new(default, -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    [DataMember] public UserId UserId { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;

    public AuthorFull() : this(default, 0) { }

    // This record relies on version-based equality
    public bool Equals(AuthorFull? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}

[DataContract]
public sealed record AuthorDiff : RecordDiff
{
    [DataMember] public Symbol? AvatarId { get; init; }
    [DataMember] public bool? IsAnonymous { get; init; }
    [DataMember] public bool? HasLeft { get; init; }
}
