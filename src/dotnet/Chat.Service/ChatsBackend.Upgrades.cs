using System.Security.Claims;
using ActualChat.Hosting;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    // [CommandHandler]
    public virtual async Task UpgradeChat(IChatsBackend.UpgradeChatCommand command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Get(invChat.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        chatId = chatId.RequireNonEmpty("Command.ChatId");
        var dbChat = await dbContext.Chats
            .Include(c => c.Owners)
            .SingleOrDefaultAsync(c => c.Id == chatId, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            return;

        Log.LogInformation("Upgrading chat #{ChatId}: '{ChatTitle}' ({ChatType})",
            chatId, dbChat.Title, dbChat.ChatType);

        var chat = dbChat.ToModel();
        var isPeer = chat.ChatType is ChatType.Peer;
        var parsedChatId = new ParsedChatId(chatId);
        parsedChatId = isPeer ? parsedChatId.AssertPeerFull() : parsedChatId.AssertGroup();
        if (chat.ChatType is ChatType.Peer) {
            // Peer chat
            var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
            var ownerUserIds = new[] { userId1.Value, userId2.Value };
            await ownerUserIds
                .Select(userId => ChatAuthorsBackend.GetOrCreate(chatId, userId, true, cancellationToken))
                .Collect(0)
                .ConfigureAwait(false);
            var tContact1 = UserContactsBackend.GetOrCreate(userId1, userId2, cancellationToken);
            var tContact2 = UserContactsBackend.GetOrCreate(userId2, userId1, cancellationToken);
            var (contact1, contact2) = await tContact1.Join(tContact2).ConfigureAwait(false);
        }
        else {
            // Group chat

            // Removing duplicate system roles
            var dbSystemRoles = await dbContext.ChatRoles
                .Where(r => r.ChatId == chatId && r.SystemRole != SystemChatRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var group in dbSystemRoles.GroupBy(r => r.SystemRole)) {
                if (group.Count() <= 1)
                    continue;
                foreach (var dbChatRole in group.Skip(1))
                    dbContext.ChatRoles.Remove(dbChatRole);
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Reload system roles
            dbSystemRoles = await dbContext.ChatRoles.Where(r => r.ChatId == chatId && r.SystemRole != SystemChatRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var ownerUserIds = dbChat.Owners.Select(o => o.DbUserId).ToArray();
            var ownerAuthors = await ownerUserIds
                .Select(userId => ChatAuthorsBackend.GetOrCreate(chatId, userId, true, cancellationToken))
                .Collect()
                .ConfigureAwait(false);

            if (ownerUserIds.Length > 0) {
                var dbOwnerRole = dbSystemRoles.SingleOrDefault(r => r.SystemRole == SystemChatRole.Owner);
                if (dbOwnerRole == null) {
                    var createOwnersRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, "", null, new() {
                        Create = new ChatRoleDiff() {
                            SystemRole = SystemChatRole.Owner,
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
                    var ownerRoleAuthorIds = (await dbContext2.ChatAuthors
                        .Where(a => a.ChatId == chatId && a.UserId != null && a.Roles.Any(r => r.DbChatRoleId == dbOwnerRole.Id))
                        .Select(a => a.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                        .Select(x => (Symbol)x)
                        .ToHashSet();
                    var missingAuthors = ownerAuthors.Where(a => !ownerRoleAuthorIds.Contains(a.Id));

                    var changeOwnersRoleCmd = new IChatRolesBackend.ChangeCommand(
                        chatId, dbOwnerRole.Id, dbOwnerRole.Version,
                        new() {
                            Update = new ChatRoleDiff() {
                                Permissions = ChatPermissions.Owner,
                                AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                                    AddedItems = ImmutableArray<Symbol>.Empty.AddRange(missingAuthors.Select(a => a.Id)),
                                },
                            },
                        });
                    await Commander.Call(changeOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
                }
            }

            var dbAnyoneRole = dbSystemRoles.SingleOrDefault(r => r.SystemRole == SystemChatRole.Anyone);
            if (dbAnyoneRole == null) {
                var createAnyoneRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, "", null, new() {
                    Create = new ChatRoleDiff() {
                        SystemRole = SystemChatRole.Anyone,
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

    public virtual async Task<Chat> CreateAnnouncementsChat(IChatsBackend.CreateAnnouncementsChatCommand command, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        if (Computed.IsInvalidating()) {
            _ = Get(chatId, default);
            return default!;
        }

        var usersTempBackend = Services.GetRequiredService<IUsersTempBackend>();
        var hostInfo = Services.GetRequiredService<HostInfo>();
        var userIds = await usersTempBackend.ListUserIds(cancellationToken).ConfigureAwait(false);

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

        var cmd = new IChatsBackend.ChangeChatCommand(chatId, null, new() {
            Create = new ChatDiff {
                ChatType = ChatType.Group,
                Title = "Actual.chat Announcements",
                IsPublic = true,
            },
        }, creatorId);
        var chat = (await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false))!;

        var anyoneRole = (await ChatRolesBackend.GetSystem(chatId, SystemChatRole.Anyone, cancellationToken)
            .ConfigureAwait(false)).Require();

        var changeAnyoneRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, anyoneRole.Id, null, new() {
            Update = new ChatRoleDiff() {
                Permissions = ChatPermissions.Invite,
            },
        });
        await Commander.Call(changeAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);

        var authorsByUserId = new Dictionary<string, ChatAuthor>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            // join existent users to the chat
           var chatAuthor = await ChatAuthorsBackend.GetOrCreate(chatId, userId, false, cancellationToken).ConfigureAwait(false);
           authorsByUserId.Add(userId, chatAuthor);
        }

        var ownerRole = (await ChatRolesBackend.GetSystem(chatId, SystemChatRole.Owner, cancellationToken)
            .ConfigureAwait(false)).Require();
        var ownerAuthorIds = ImmutableArray<Symbol>.Empty;
        foreach (var userId in owners.Values) {
            if (OrdinalEquals(userId, creatorId))
                continue;
            if (!authorsByUserId.TryGetValue(userId, out var chatAuthor))
                continue;
            ownerAuthorIds = ownerAuthorIds.Add(chatAuthor.Id);
        }

        if (ownerAuthorIds.Length > 0) {
            var changeOwnerRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, ownerRole.Id, null, new() {
                Update = new ChatRoleDiff {
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
    public virtual async Task FixCorruptedChatReadPositions(IChatsBackend.FixCorruptedChatReadPositionsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var chatReadPositionsBackend = Services.GetRequiredService<IChatReadPositionsBackend>();
        var usersTempBackend = Services.GetRequiredService<IUsersTempBackend>();

        var userIds = await usersTempBackend.ListUserIds(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var chatIds = await ChatAuthorsBackend.ListUserChatIds(userId, cancellationToken).ConfigureAwait(false);
            foreach (var chatId in chatIds) {
                var lastReadEntryId = await chatReadPositionsBackend.Get(userId, chatId, cancellationToken).ConfigureAwait(false);
                if (lastReadEntryId.GetValueOrDefault() == 0)
                    continue;

                var idRange = await GetIdRange(chatId, ChatEntryType.Text, false, cancellationToken).ConfigureAwait(false);
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
                var setCmd = new IChatReadPositionsBackend.SetCommand(userId, chatId,  lastEntryId - 1, true);
                await Commander.Call(setCmd, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
