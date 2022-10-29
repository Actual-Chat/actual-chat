using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record AuthorFull : Author
{
    public static new Requirement<AuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.Author.Unavailable()),
        (AuthorFull? a) => a is { Id.IsEmpty : false });

    public static new AuthorFull None { get; } = new() { Avatar = Avatar.None };
    public static new AuthorFull Loading { get; } = new() { Avatar = Avatar.Loading }; // Should differ by ref. from None

    [DataMember] public Symbol UserId { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;
}
