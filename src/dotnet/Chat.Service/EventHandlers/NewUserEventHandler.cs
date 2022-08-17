using ActualChat.Events;
using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Events;

namespace ActualChat.Chat.EventHandlers;

public class NewUserEventHandler : IEventHandler<NewUserEvent>
{
    private readonly IAccountsBackend _accountsBackend;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IChatRolesBackend _chatRolesBackend;
    private readonly ICommander _commander;
    private readonly HostInfo _hostInfo;

    public NewUserEventHandler(
        IAccountsBackend accountsBackend,
        IChatAuthorsBackend chatAuthorsBackend,
        IChatRolesBackend chatRolesBackend,
        ICommander commander,
        HostInfo hostInfo)
    {
        _hostInfo = hostInfo;
        _accountsBackend = accountsBackend;
        _chatAuthorsBackend = chatAuthorsBackend;
        _chatRolesBackend = chatRolesBackend;
        _commander = commander;
    }

    public async Task Handle(NewUserEvent @event, ICommander commander, CancellationToken cancellationToken)
    {
        await JoinToAnnouncementsChat(@event.UserId, cancellationToken).ConfigureAwait(false);

        if (_hostInfo.IsDevelopmentInstance)
            await JoinAdminToDefaultChat(@event.UserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinToAnnouncementsChat(string userId, CancellationToken cancellationToken)
        => _ = await _chatAuthorsBackend.GetOrCreate(
            Constants.Chat.AnnouncementsChatId,
            userId,
            false,
            cancellationToken).ConfigureAwait(false);

    private async Task JoinAdminToDefaultChat(string userId, CancellationToken cancellationToken)
    {
        var account = await _accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null || !account.IsAdmin)
            return;

        var chatId = Constants.Chat.DefaultChatId;
        var chatAuthor = await _chatAuthorsBackend.GetOrCreate(
                chatId,
                userId,
                false,
                cancellationToken)
            .ConfigureAwait(false);

        var systemRoles = await _chatRolesBackend.ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        var ownerRole = systemRoles.FirstOrDefault(c => c.SystemRole == SystemChatRole.Owner);
        if (ownerRole == null)
            return;
        var createOwnersRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, ownerRole.Id, null,
        new () {
            Update = new ChatRoleDiff() {
                AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                    AddedItems = ImmutableArray<Symbol>.Empty.Add(chatAuthor.Id),
                },
            },
        });
        await _commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
    }
}
