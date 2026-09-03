using ActualChat.Chat.Db;
using ActualChat.Contacts;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatsUpgradeBackend : DbServiceBase<ChatDbContext>, IChatsUpgradeBackend
{
    private ChatsBackend ChatsBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IBlobStorages Blobs { get; }
    private IMediaBackend MediaBackend { get; }

    public ChatsUpgradeBackend(IServiceProvider services) : base(services)
    {
        ChatsBackend = (ChatsBackend)services.GetRequiredService<IChatsBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        Blobs = Services.GetRequiredService<IBlobStorages>();
        MediaBackend = services.GetRequiredService<IMediaBackend>();
    }

    // [CommandHandler]
    public virtual async Task OnUpgradeChat(
        ChatsUpgradeBackend_UpgradeChat command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): Currently this command just "repairs" some of chat properties,
        // even though originally it was upgrading DbChat.Owners to roles & authors.
        //
        // This part isn't there anymore, coz Owners are gone,
        // and there is no code calling this command.
        //
        // I left it here mainly "just in case" - e.g. if in future we'll end up using
        // exactly the same command to perform chat upgrades (though migrations are
        // certainly preferable for that).

        var chatId = command.ChatId.Require();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChat = context.Operation.Items.KeylessGet<Chat>()!;
            _ = ChatsBackend.Get(invChat.Id, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .SingleOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            return;

        Log.LogInformation("Upgrading chat #{ChatId}: '{ChatTitle}' ({ChatType})",
            chatId, dbChat.Title, dbChat.Kind);

        var chat = dbChat.ToModel();
        if (chat.Id is PeerChatId peerChatId) {
            // Peer chat
            await peerChatId.UserIds
                .ToArray()
                .Select(userId => AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
        }
        else {
            // Group chat

            // Removing duplicate system roles
            var systemDbRoles = await dbContext.Roles
                .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var group in systemDbRoles.GroupBy(r => r.SystemRole)) {
                if (group.Count() <= 1)
                    continue;
                foreach (var dbChatRole in group.Skip(1))
                    dbContext.Roles.Remove(dbChatRole);
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Reload system roles
            systemDbRoles = await dbContext.Roles
                .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var dbAnyoneRole = systemDbRoles.SingleOrDefault(r => r.SystemRole == SystemRole.Anyone);
            if (dbAnyoneRole == null) {
                var createAnyoneRoleCmd = new RolesBackend_Change(chatId, default, null, new() {
                    Create = new RoleDiff() {
                        SystemRole = SystemRole.Anyone,
                        Permissions =
                            ChatPermissions.Write
                            | ChatPermissions.Invite
                            | ChatPermissions.SeeMembers
                            | ChatPermissions.Leave,
                    },
                });
                await Commander.Call(createAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation.Items.KeylessSet(chat);
    }

    private async Task<UserId[]> ListAllAccountIds(CancellationToken cancellationToken)
    {
        var result = new List<UserId>();
        UserId? lastId = null;
        long minVersion = 0;
        while (true) {
            var batch = await AccountsBackend.ListChanged(minVersion, long.MaxValue, lastId, 1000, cancellationToken)
                .ConfigureAwait(false);
            if (batch.Length == 0)
                break;
            result.AddRange(batch.Select(x => x.Id));
            var last = batch[^1];
            lastId = last.Id;
            minVersion = last.Version;
        }
        return result.ToArray();
    }
}
