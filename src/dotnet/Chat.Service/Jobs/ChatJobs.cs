using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Jobs;

namespace ActualChat.Chat.Jobs;

public class ChatJobs
{
    private HostInfo HostInfo { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChatRolesBackend ChatRolesBackend { get; }
    private ICommander Commander { get; }

    public ChatJobs(
        HostInfo hostInfo,
        IChatAuthorsBackend chatAuthorsBackend,
        IAccountsBackend accountsBackend,
        IChatRolesBackend chatRolesBackend,
        ICommander commander)
    {
        HostInfo = hostInfo;
        ChatAuthorsBackend = chatAuthorsBackend;
        AccountsBackend = accountsBackend;
        ChatRolesBackend = chatRolesBackend;
        Commander = commander;
    }

    [CommandHandler]
    public virtual async Task OnNewUserJob(OnNewUserJob job, CancellationToken cancellationToken)
    {
        await JoinToAnnouncementsChat(job.UserId, cancellationToken).ConfigureAwait(false);

        if (HostInfo.IsDevelopmentInstance)
            await JoinAdminToDefaultChat(job.UserId, cancellationToken).ConfigureAwait(false);
    }



    private async Task JoinToAnnouncementsChat(string userId, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        var chatAuthor = await ChatAuthorsBackend.GetOrCreate(
                chatId,
                userId,
                false,
                cancellationToken)
            .ConfigureAwait(false);

        if (!HostInfo.IsDevelopmentInstance)
            return;

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null || !account.IsAdmin)
            return;

        await AddOwner(chatId, chatAuthor, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinAdminToDefaultChat(string userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null || !account.IsAdmin)
            return;

        var chatId = Constants.Chat.DefaultChatId;
        var chatAuthor = await ChatAuthorsBackend.GetOrCreate(
                chatId,
                userId,
                false,
                cancellationToken)
            .ConfigureAwait(false);

        await AddOwner(chatId, chatAuthor, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddOwner(string chatId, ChatAuthor chatAuthor, CancellationToken cancellationToken)
    {
        var ownerRole = await ChatRolesBackend.GetSystem(chatId, SystemChatRole.Owner, cancellationToken)
            .ConfigureAwait(false);
        if (ownerRole == null)
            return;

        var createOwnersRoleCmd = new IChatRolesBackend.ChangeCommand(chatId,
            ownerRole.Id,
            null,
            new Change<ChatRoleDiff> {
                Update = new ChatRoleDiff {
                    AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol> {
                        AddedItems = ImmutableArray<Symbol>.Empty.Add(chatAuthor.Id),
                    },
                },
            });
        await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
    }
}
