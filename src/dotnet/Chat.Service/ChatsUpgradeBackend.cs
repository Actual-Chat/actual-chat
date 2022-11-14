using System.Security.Claims;
using ActualChat.Chat.Db;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatsUpgradeBackend : DbServiceBase<ChatDbContext>, IChatsUpgradeBackend
{
    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IAuthorsUpgradeBackend AuthorsUpgradeBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IChatsBackend Backend { get; }

    public ChatsUpgradeBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        AuthorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        Backend = services.GetRequiredService<IChatsBackend>();
    }

    // [CommandHandler]
    public virtual async Task UpgradeChat(
        IChatsUpgradeBackend.UpgradeChatCommand command,
        CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Backend.Get(invChat.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        chatId = chatId.RequireNonEmpty("Command.ChatId");
        var dbChat = await dbContext.Chats
            .Include(c => c.Owners)
            .SingleOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            return;

        Log.LogInformation("Upgrading chat #{ChatId}: '{ChatTitle}' ({ChatType})",
            chatId, dbChat.Title, dbChat.Kind);

        var chat = dbChat.ToModel();
        var isPeer = chat.Kind is ChatKind.Peer;
        var parsedChatId = new ChatId(chatId);
        parsedChatId = isPeer ? parsedChatId.RequirePeerChatId() : parsedChatId.RequireGroupChatId();
        if (chat.Kind is ChatKind.Peer) {
            // Peer chat
            var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
            var ownerUserIds = new[] { userId1.Value, userId2.Value };
            await ownerUserIds
                .Select(userId => AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken))
                .Collect(0)
                .ConfigureAwait(false);
            var tContact1 = ContactsBackend.GetOrCreateUserContact(userId1, userId2, cancellationToken);
            var tContact2 = ContactsBackend.GetOrCreateUserContact(userId2, userId1, cancellationToken);
            var (contact1, contact2) = await tContact1.Join(tContact2).ConfigureAwait(false);
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

            var ownerUserIds = dbChat.Owners.Select(o => o.DbUserId).ToArray();
            var ownerAuthors = await ownerUserIds
                .Select(userId => AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken))
                .Collect()
                .ConfigureAwait(false);

            if (ownerUserIds.Length > 0) {
                var dbOwnerRole = systemDbRoles.SingleOrDefault(r => r.SystemRole == SystemRole.Owner);
                if (dbOwnerRole == null) {
                    var createOwnersRoleCmd = new IRolesBackend.ChangeCommand(chatId, "", null, new() {
                        Create = new RoleDiff() {
                            SystemRole = SystemRole.Owner,
                            Permissions = ChatPermissions.Owner,
                            AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                                AddedItems = ImmutableArray<Symbol>.Empty.AddRange(ownerAuthors.Select(a => a.Id)),
                            },
                        },
                    });
                    await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
                }
                else {
                    // We want another transaction view here
                    using var dbContext2 = CreateDbContext();
                    var ownerRoleAuthorIds = (await dbContext2.Authors
                        .Where(a => a.ChatId == chatId.Value && a.UserId != null && a.Roles.Any(r => r.DbRoleId == dbOwnerRole.Id))
                        .Select(a => a.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                        .Select(x => (Symbol)x)
                        .ToHashSet();
                    var missingAuthors = ownerAuthors.Where(a => !ownerRoleAuthorIds.Contains(a.Id));

                    var changeOwnersRoleCmd = new IRolesBackend.ChangeCommand(
                        chatId, dbOwnerRole.Id, dbOwnerRole.Version,
                        new() {
                            Update = new RoleDiff() {
                                Permissions = ChatPermissions.Owner,
                                AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                                    AddedItems = ImmutableArray<Symbol>.Empty.AddRange(missingAuthors.Select(a => a.Id)),
                                },
                            },
                        });
                    await Commander.Call(changeOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
                }
            }

            var dbAnyoneRole = systemDbRoles.SingleOrDefault(r => r.SystemRole == SystemRole.Anyone);
            if (dbAnyoneRole == null) {
                var createAnyoneRoleCmd = new IRolesBackend.ChangeCommand(chatId, "", null, new() {
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

        // NOTE(AY): Uncomment this once we're completely sure there are no issues w/ owners -> roles upgrade
        // dbChat.Owners.Clear();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
    }

    // [CommandHandler]
    public virtual async Task<Chat> CreateAnnouncementsChat(
        IChatsUpgradeBackend.CreateAnnouncementsChatCommand command,
        CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        if (Computed.IsInvalidating()) {
            _ = Backend.Get(chatId, default);
            return default!;
        }

        var usersTempBackend = Services.GetRequiredService<IUsersUpgradeBackend>();
        var hostInfo = Services.GetRequiredService<HostInfo>();
        var userIds = await usersTempBackend.ListAllUserIds(cancellationToken).ConfigureAwait(false);

        string? creatorId = null;

        var adminUser = await AccountsBackend.Get(UserConstants.Admin.UserId, cancellationToken).ConfigureAwait(false);
        if (adminUser != null)
            creatorId = adminUser.Id;

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            var user = account?.User;
            if (user == null)
                continue;
            if (user.Claims.Count == 0)
                continue;
            if (!user.Claims.TryGetValue(ClaimTypes.Email, out var email))
                continue;

            if (hostInfo.IsDevelopmentInstance) {
                if (email.OrdinalIgnoreCaseEndsWith("actual.chat"))
                    owners.Add(email, userId);
            }
            else {
                if (OrdinalIgnoreCaseEquals(email, "alex.yakunin@actual.chat") || OrdinalIgnoreCaseEquals(email, "alexey.kochetov@actual.chat"))
                    owners.Add(email, userId);
            }
        }

        if (creatorId == null) {
            if (owners.TryGetValue("alex.yakunin@actual.chat", out var temp))
                creatorId = temp;
            else if (owners.Count > 0)
                creatorId = owners.First().Value;
        }

        if (creatorId == null)
            throw StandardError.Constraint("Creator user not found");

        var changeCommand = new IChatsBackend.ChangeCommand(chatId, null, new() {
            Create = new ChatDiff {
                Kind = ChatKind.Group,
                Title = "Actual.chat Announcements",
                IsPublic = true,
            },
        }, creatorId);
        var chat = (await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false))!;

        var anyoneRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Anyone, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var changeAnyoneRoleCmd = new IRolesBackend.ChangeCommand(chatId, anyoneRole.Id, null, new() {
            Update = new RoleDiff() {
                Permissions = ChatPermissions.Invite,
            },
        });
        await Commander.Call(changeAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);

        var authorsByUserId = new Dictionary<string, AuthorFull>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            // join existent users to the chat
           var author = await AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken).ConfigureAwait(false);
           authorsByUserId.Add(userId, author);
        }

        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var ownerAuthorIds = ImmutableArray<Symbol>.Empty;
        foreach (var userId in owners.Values) {
            if (OrdinalEquals(userId, creatorId))
                continue;
            if (!authorsByUserId.TryGetValue(userId, out var author))
                continue;
            ownerAuthorIds = ownerAuthorIds.Add(author.Id);
        }

        if (ownerAuthorIds.Length > 0) {
            var changeOwnerRoleCmd = new IRolesBackend.ChangeCommand(chatId, ownerRole.Id, null, new() {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol> {
                        AddedItems = ownerAuthorIds
                    }
                },
            });
            await Commander.Call(changeOwnerRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        return chat;
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

                var idRange = await Backend.GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
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
