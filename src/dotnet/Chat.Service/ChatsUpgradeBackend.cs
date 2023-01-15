using ActualChat.Chat.Db;
using ActualChat.Contacts;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatsUpgradeBackend : DbServiceBase<ChatDbContext>, IChatsUpgradeBackend
{
    private ChatsBackend ChatsBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IAuthorsUpgradeBackend AuthorsUpgradeBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IUsersUpgradeBackend UsersUpgradeBackend { get; }
    private IBlobStorageProvider Blobs { get; }

    public ChatsUpgradeBackend(IServiceProvider services) : base(services)
    {
        ChatsBackend = (ChatsBackend)services.GetRequiredService<IChatsBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        AuthorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        UsersUpgradeBackend = services.GetRequiredService<IUsersUpgradeBackend>();
        Blobs = Services.GetRequiredService<IBlobStorageProvider>();
    }

    // [CommandHandler]
    public virtual async Task UpgradeChat(
        IChatsUpgradeBackend.UpgradeChatCommand command,
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

        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = ChatsBackend.Get(invChat.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .SingleOrDefaultAsync(c => c.Id == chatId, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            return;

        Log.LogInformation("Upgrading chat #{ChatId}: '{ChatTitle}' ({ChatType})",
            chatId, dbChat.Title, dbChat.Kind);

        var chat = dbChat.ToModel();
        if (chat.Id.IsPeerChatId(out var peerChatId)) {
            // Peer chat
            await peerChatId.UserIds
                .ToArray()
                .Select(userId => AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken))
                .Collect(0)
                .ConfigureAwait(false);
        }
        else {
            // Group chat

            // Removing duplicate system roles
            var systemDbRoles = await dbContext.Roles
                .Where(r => r.ChatId == chatId && r.SystemRole != SystemRole.None)
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
                .Where(r => r.ChatId == chatId && r.SystemRole != SystemRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var dbAnyoneRole = systemDbRoles.SingleOrDefault(r => r.SystemRole == SystemRole.Anyone);
            if (dbAnyoneRole == null) {
                var createAnyoneRoleCmd = new IRolesBackend.ChangeCommand(chatId, default, null, new() {
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
        context.Operation().Items.Set(chat);
    }

    // [CommandHandler]
    public virtual async Task FixCorruptedReadPositions(
        IChatsUpgradeBackend.FixCorruptedReadPositionsCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var readPositionsBackend = Services.GetRequiredService<IReadPositionsBackend>();
        var usersTempBackend = Services.GetRequiredService<IUsersUpgradeBackend>();

        var userIds = await usersTempBackend.ListAllUserIds(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var chatIds = await AuthorsUpgradeBackend.ListChatIds(userId, cancellationToken).ConfigureAwait(false);
            foreach (var chatId in chatIds) {
                var lastReadEntryId = await readPositionsBackend.Get(userId, chatId, cancellationToken).ConfigureAwait(false);
                if (lastReadEntryId.GetValueOrDefault() == 0)
                    continue;

                var idRange = await ChatsBackend.GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
                var lastEntryId = idRange.End - 1;
                if (lastEntryId >= lastReadEntryId)
                    continue;

                // since it was corrupted for some time and user might not know that there are some new message
                // let's show at least 1 unread message, so user could pay attention to this chat
                Log.LogInformation(
                    "Fixing corrupted last read position for user #{UserId} at chat #{ChatId}: {CurrentLastReadEntryId} -> {FixedLastReadEntryId}",
                    userId,
                    chatId,
                    lastReadEntryId,
                    lastEntryId - 1);
                var setCmd = new IReadPositionsBackend.SetCommand(userId, chatId,  lastEntryId - 1, true);
                await Commander.Call(setCmd, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
