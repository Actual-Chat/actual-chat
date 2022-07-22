using System.Security;
using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatAuthor : Author, IRequirementTarget
{
    public static Requirement<ChatAuthor> MustExist { get; } = Requirement.New(
        new(() => new SecurityException("You are not a participant of this chat.")),
        (ChatAuthor? p) => p != null);

    [DataMember] public Symbol ChatId { get; init; }
    [DataMember] public Symbol UserId { get; init; }
    [DataMember] public bool HasLeft { get; init; }
    [DataMember] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;
}
