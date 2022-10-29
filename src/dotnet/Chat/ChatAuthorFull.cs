using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatAuthorFull : ChatAuthor
{
    public static new Requirement<ChatAuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.ChatAuthor.Unavailable()),
        (ChatAuthorFull? a) => a is { Id.IsEmpty : false });

    public static new ChatAuthorFull None { get; } = new() { Avatar = Avatar.None };
    public static new ChatAuthorFull Loading { get; } = new() { Avatar = Avatar.Loading }; // Should differ by ref. from None

    [DataMember] public Symbol UserId { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;
}
