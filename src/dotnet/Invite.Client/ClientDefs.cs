using RestEase;

namespace ActualChat.Invite.Client;

[BasePath("invites")]
public interface IInvitesClientDef
{
    [Get(nameof(GetUserInvites))]
    Task<ImmutableArray<Invite>> GetUserInvites(Session session, CancellationToken cancellationToken);

    [Get(nameof(GetChatInvites))]
    Task<ImmutableArray<Invite>> GetChatInvites(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Generate))]
    Task<Invite> Generate([Body] IInvites.GenerateCommand command, CancellationToken cancellationToken);

    [Post(nameof(Use))]
    Task<Invite> Use([Body] IInvites.UseCommand command, CancellationToken cancellationToken);
}
