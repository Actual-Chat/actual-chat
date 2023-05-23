using RestEase;

namespace ActualChat.Invite;

[BasePath("invites")]
public interface IInvitesClientDef
{
    [Get(nameof(ListUserInvites))]
    Task<ImmutableArray<Invite>> ListUserInvites(Session session, CancellationToken cancellationToken);

    [Get(nameof(ListChatInvites))]
    Task<ImmutableArray<Invite>> ListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Post(nameof(Generate))]
    Task<Invite> Generate([Body] IInvites.GenerateCommand command, CancellationToken cancellationToken);

    [Post(nameof(Use))]
    Task<Invite> Use([Body] IInvites.UseCommand command, CancellationToken cancellationToken);

    [Post(nameof(Revoke))]
    Task Revoke([Body] IInvites.RevokeCommand command, CancellationToken cancellationToken);
}
