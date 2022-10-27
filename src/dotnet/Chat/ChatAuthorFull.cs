using System.Security;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatAuthorFull : ChatAuthor
{
    public static Requirement<ChatAuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.ChatAuthor.Unavailable()),
        (ChatAuthorFull? a) => !ReferenceEquals(a, null));

    [DataMember] public Symbol UserId { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;
}
