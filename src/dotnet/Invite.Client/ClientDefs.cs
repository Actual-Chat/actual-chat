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
    Task<Invite> Generate([Body] Invites_Generate command, CancellationToken cancellationToken);

    [Post(nameof(Use))]
    Task<Invite> Use([Body] Invites_Use command, CancellationToken cancellationToken);

    [Post(nameof(Revoke))]
    Task Revoke([Body] Invites_Revoke command, CancellationToken cancellationToken);
}
