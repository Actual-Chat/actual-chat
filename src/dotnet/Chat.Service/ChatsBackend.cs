using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Events;
using ActualChat.Hosting;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Versioning;

namespace ActualChat.Chat;

public class ChatsBackend : DbServiceBase<ChatDbContext>, IChatsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private static readonly string ChatIdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private static readonly RandomStringGenerator ChatIdGenerator = new(10, ChatIdAlphabet);

    private IAuthBackend AuthBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IChatRolesBackend ChatRolesBackend { get; }
    private IUserContactsBackend UserContactsBackend { get; }
    private IDbEntityResolver<string, DbChat> DbChatResolver { get; }
    private IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> DbChatEntryIdGenerator { get; }
    private IEventPublisher EventPublisher { get; }
    private DiffEngine DiffEngine { get; }

    public ChatsBackend(IServiceProvider services) : base(services)
    {
        AuthBackend = Services.GetRequiredService<IAuthBackend>();
        AccountsBackend = Services.GetRequiredService<IAccountsBackend>();
        ChatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        ChatRolesBackend = Services.GetRequiredService<IChatRolesBackend>();
        UserContactsBackend = services.GetRequiredService<IUserContactsBackend>();
        DbChatResolver = Services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
        DbChatEntryIdGenerator = Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        EventPublisher = Services.GetRequiredService<IEventPublisher>();
        DiffEngine = Services.GetRequiredService<DiffEngine>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(chatId);
        var dbChat = await DbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorRules> GetRules(
        string chatId,
        string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertValid();

        // Peer chat: we don't use actual roles to determine rules here
        var chatType = parsedChatId.Kind.ToChatType();
        if (chatType is ChatType.Peer)
            return await GetPeerChatRules(chatId, chatPrincipalId, cancellationToken).ConfigureAwait(false);

        // Group chat
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ChatAuthorRules.None(chatId);

        ParseChatPrincipalId(chatPrincipalId, out var authorId, out var userId);

        ChatAuthor? author = null;
        if (!authorId.IsNullOrEmpty()) {
            author = await ChatAuthorsBackend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
            userId = author?.UserId;
        }
        var account = userId.IsNullOrEmpty() ? null
            : await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);

        var roles = ImmutableArray<ChatRole>.Empty;
        var isJoined = author is { HasLeft: false };
        if (isJoined) {
            var isAuthenticated = account != null;
            var isAnonymous = author is { IsAnonymous: true };
            roles = await ChatRolesBackend
                .List(chatId, author!.Id, isAuthenticated, isAnonymous, cancellationToken)
                .ConfigureAwait(false);
        }
        var permissions = roles.ToPermissions();
        if (chat.IsPublic) {
            permissions |= ChatPermissions.Join;
            if (!isJoined) {
                var anyoneSystemRole = await ChatRolesBackend.GetSystem(chatId, SystemChatRole.Anyone, cancellationToken).ConfigureAwait(false);
                if (anyoneSystemRole != null && anyoneSystemRole.Permissions.Has(ChatPermissions.SeeMembers))
                    permissions |= ChatPermissions.SeeMembers;
            }
        }
        permissions = permissions.AddImplied();

        var rules = new ChatAuthorRules(chatId, author, account, permissions);
        return rules;
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Type == entryType);
        if (!includeRemoved)
            dbEntries = dbEntries.Where(e => !e.IsRemoved);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbEntries = dbEntries
                .Where(e => e.Id >= idRangeValue.Start && e.Id < idRangeValue.End);
        }

        return await dbEntries.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        var minId = await GetMinId(chatId, entryType, cancellationToken).ConfigureAwait(false);
        var maxId = await GetMaxId(chatId, entryType, cancellationToken).ConfigureAwait(false);
        return (minId, maxId + 1);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetMinId(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        return await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Type == entryType)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetMaxId(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        return await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Type == entryType)
            .OrderByDescending(e => e.Id)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile0(string chatId, ChatEntryType entryType, CancellationToken cancellationToken)
    {
        using var _ = Computed.SuspendDependencyCapture();
        var maxId = await GetMaxId(chatId, entryType, cancellationToken).ConfigureAwait(false);
        return IdTileStack.Layers[0].GetTile(maxId).Range;
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetLastIdTile1(string chatId, ChatEntryType entryType, CancellationToken cancellationToken)
    {
        using var _ = Computed.SuspendDependencyCapture();
        var maxId = await GetMaxId(chatId, entryType, cancellationToken).ConfigureAwait(false);
        return IdTileStack.Layers[1].GetTile(maxId).Range;
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(idTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = new List<ChatTile>();
            foreach (var smallerIdTile in smallerIdTiles) {
                var smallerChatTile = await GetTile(chatId, entryType, smallerIdTile.Range, includeRemoved, cancellationToken)
                    .ConfigureAwait(false);
                smallerChatTiles.Add(smallerChatTile);
            }
            return new ChatTile(smallerChatTiles, includeRemoved);
        }
        if (!includeRemoved) {
            var fullTile = await GetTile(chatId, entryType, idTileRange, true, cancellationToken).ConfigureAwait(false);
            return new ChatTile(idTileRange, false, fullTile.Entries.Where(e => !e.IsRemoved).ToImmutableArray());
        }

        // If we're here, it's the smallest tile & includeRemoved = true
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId
                && e.Type == entryType
                && e.Id >= idTile.Range.Start
                && e.Id < idTile.Range.End)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbEntries.Select(e => e.ToModel()).ToImmutableArray();
        return new ChatTile(idTileRange, true, entries);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        string chatId, long entryId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAttachments = await dbContext.TextEntryAttachments
            .Where(a => a.ChatId == chatId && a.EntryId == entryId)
            .OrderBy(e => e.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var attachments = dbAttachments.Select(e => e.ToModel()).ToImmutableArray();
        return attachments;
    }

    // Command handlers

    // [CommandHandler]
    public virtual async Task<Chat?> ChangeChat(
        IChatsBackend.ChangeChatCommand command,
        CancellationToken cancellationToken)
    {
        var (chatId, expectedVersion, change, creatorUserId) = command;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Get(invChat.Id, default);
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        Chat chat;
        DbChat dbChat;
        if (change.RequireValid().IsCreate(out var update)) {
            chatId = chatId.NullIfEmpty() ?? ChatIdGenerator.Next();
            chat = new Chat() {
                Id = chatId,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            chat = DiffEngine.Patch(chat, update);
            if (chat.ChatType is not ChatType.Peer && chat.Title.IsNullOrEmpty())
                throw new ValidationException("Chat title cannot be empty.");

            var isPeer = chat.ChatType is ChatType.Peer;
            var parsedChatId = new ParsedChatId(chatId);
            parsedChatId = isPeer ? parsedChatId.AssertPeerFull() : parsedChatId.AssertGroup();

            dbChat = new DbChat(chat);
            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (chat.ChatType is ChatType.Peer) {
                // Peer chat
                creatorUserId.RequireEmpty("Command.CreatorUserId");
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
                creatorUserId = creatorUserId.RequireNonEmpty("Command.CreatorUserId");
                var chatAuthor = await ChatAuthorsBackend
                    .GetOrCreate(chatId, creatorUserId, true, cancellationToken)
                    .ConfigureAwait(false);

                var createOwnersRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, "", null, new() {
                    Create = new ChatRoleDiff() {
                        SystemRole = SystemChatRole.Owner,
                        Permissions = ChatPermissions.Owner,
                        AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                            AddedItems = ImmutableArray<Symbol>.Empty.Add(chatAuthor.Id),
                        },
                    },
                });
                await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);

                var createJoinedRoleCmd = new IChatRolesBackend.ChangeCommand(chatId, "", null, new() {
                    Create = new ChatRoleDiff() {
                        SystemRole = SystemChatRole.Anyone,
                        Permissions =
                            ChatPermissions.Write
                            | ChatPermissions.Invite
                            | ChatPermissions.SeeMembers
                            | ChatPermissions.Leave,
                    },
                });
                await Commander.Call(createJoinedRoleCmd, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (change.IsUpdate(out update)) {
            chatId = chatId.RequireNonEmpty("Command.ChatId");
            dbChat = await dbContext.Chats
                .SingleAsync(a => a.Id == chatId, cancellationToken)
                .ConfigureAwait(false);
            chat = dbChat.ToModel();
            if ((update.ChatType ?? chat.ChatType) != chat.ChatType)
                throw StandardError.Constraint("Chat type cannot be changed.");
            VersionChecker.RequireExpected(chat.Version, expectedVersion);

            chat = chat with {
                Version = VersionGenerator.NextVersion(chat.Version),
            };
            chat = DiffEngine.Patch(chat, update);
            if (chat.ChatType is not ChatType.Peer && chat.Title.IsNullOrEmpty())
                throw new ValidationException("Chat title cannot be empty.");
            dbChat.UpdateFrom(chat);
            dbContext.Update(dbChat);
        }
        else
            throw StandardError.NotSupported("Chat removal is not supported yet.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;
    }

    [CommandHandler(IsFilter = true, Priority = 1001)] // 1000 = DbOperationScopeProvider, we must "wrap" it
    protected virtual async Task OnUpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        // Creates peer chat for direct conversations between chat authors

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var chatId = command.Entry.ChatId;
        var parsedChatId = new ParsedChatId(chatId).AssertValid();
        var isPeer = parsedChatId.Kind.IsPeerAny();
        if (isPeer)
            _ = await GetOrCreatePeerChat(chatId, cancellationToken).ConfigureAwait(false);

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> UpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        var entry = command.Entry;
        var context = CommandContext.GetCurrent();
        var isUpdate = entry.Id != 0;
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            if (invChatEntry != null)
                InvalidateChatPages(entry.ChatId, entry.Type, invChatEntry.Id, isUpdate);
            var invIsNew = context.Operation().Items.GetOrDefault(false);
            if (invIsNew)
                _ = GetMaxId(entry.ChatId, entry.Type, default); // We invalidate min-max Id range at last
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // TODO: Use entity resolver or remove this check (?)
        var isNew = entry.Id == 0;
        var dbEntry = await DbUpsertEntry(dbContext, entry, cancellationToken).ConfigureAwait(false);

        entry = dbEntry.ToModel();
        context.Operation().Items.Set(entry);
        context.Operation().Items.Set(isNew);

        if (!entry.Content.IsNullOrEmpty() && !entry.IsStreaming && entry.Type == ChatEntryType.Text) {
            var chatEvent = new NewChatEntryEvent(entry.ChatId, entry.Id, entry.AuthorId, entry.Content);
            await EventPublisher.Publish(chatEvent, cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    public virtual async Task<TextEntryAttachment> CreateTextEntryAttachment(IChatsBackend.CreateTextEntryAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            _ = GetTextEntryAttachments(command.Attachment.ChatId, command.Attachment.EntryId, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var attachment = command.Attachment with {
            Version = VersionGenerator.NextVersion(),
        };
        var dbAttachment = new DbTextEntryAttachment(attachment);
        dbContext.Add(dbAttachment);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        attachment = dbAttachment.ToModel();
        context.Operation().Items.Set(attachment);
        return attachment;
    }

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
        var userIds = await usersTempBackend.GetUserIds(cancellationToken).ConfigureAwait(false);

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
                if (email.EndsWith("actual.chat", StringComparison.OrdinalIgnoreCase))
                    owners.Add(email, userId);
            }
            else {
                if (email.Equals("alex.yakunin@actual.chat", StringComparison.OrdinalIgnoreCase)
                    || email.Equals("alexey.kochetov@actual.chat", StringComparison.OrdinalIgnoreCase)) {
                    owners.Add(email, userId);
                }
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
            if (string.Equals(userId, creatorId, StringComparison.Ordinal))
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

    // Protected methods

    protected void InvalidateChatPages(string chatId, ChatEntryType entryType, long entryId, bool isUpdate)
    {
        if (!isUpdate) {
            _ = GetEntryCount(chatId, entryType, null, false, default);
            _ = GetEntryCount(chatId, entryType, null, true, default);
            var tile0 = IdTileStack.Layers[0].GetTile(entryId);
            var tile1 = IdTileStack.Layers[1].GetTile(entryId);
            if (tile0.Start == entryId)
                _ = GetLastIdTile0(chatId, entryType, default);
            if (tile1.Start == entryId)
                _ = GetLastIdTile1(chatId, entryType, default);
        }
        foreach (var idTile in IdTileStack.GetAllTiles(entryId)) {
            if (idTile.Layer.Smaller == null) {
                // Larger tiles are composed out of smaller tiles,
                // so we have to invalidate just the smallest one.
                // And the tile with includeRemoved == false is based on
                // a tile with includeRemoved == true, so we have to invalidate
                // just this tile.
                _ = GetTile(chatId, entryType, idTile.Range, true, default);
            }
            if (!isUpdate) {
                _ = GetEntryCount(chatId, entryType, idTile.Range, true, default);
                _ = GetEntryCount(chatId, entryType, idTile.Range, false, default);
            }
        }
    }

    protected async Task<DbChatEntry> DbUpsertEntry(
        ChatDbContext dbContext,
        ChatEntry entry,
        CancellationToken cancellationToken)
    {
        // AK: Suspicious - probably can lead to performance issues
        // AY: Yes, but the goal is to have a dense sequence here;
        //     later we'll change this to something that's more performant.
        var isNew = entry.Id == 0;
        var entryType = entry.Type;
        DbChatEntry dbEntry;
        if (isNew) {
            var id = await DbNextEntryId(dbContext, entry.ChatId, entryType, cancellationToken).ConfigureAwait(false);
            entry = entry with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
            };
            dbEntry = new (entry);
            dbContext.Add(dbEntry);
        }
        else {
            var compositeId = DbChatEntry.ComposeId(entry.ChatId, entryType, entry.Id);
            dbEntry = await dbContext.ChatEntries
                .FindAsync(DbKey.Compose(compositeId), cancellationToken)
                .ConfigureAwait(false)
                ?? throw StandardError.NotFound<ChatEntry>();
            VersionChecker.RequireExpected(dbEntry.Version, entry.Version);
            entry = entry with {
                Version = VersionGenerator.NextVersion(dbEntry.Version),
            };
            dbEntry.UpdateFrom(entry);
            dbContext.Update(dbEntry);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbEntry;
    }

    // Private / internal methods

    internal Task<long> DbNextEntryId(
        ChatDbContext dbContext,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => DbChatEntryIdGenerator.Next(dbContext, new DbChatEntryShardRef(chatId, entryType), cancellationToken);

    private void ParseChatPrincipalId(string chatPrincipalId, out string? authorId, out string? userId)
    {
        if (chatPrincipalId.OrdinalContains(":")) {
            authorId = chatPrincipalId;
            userId = null;
        }
        else {
            authorId = null;
            userId = chatPrincipalId;
        }
    }

    private async Task<ChatAuthorRules> GetPeerChatRules(
        string chatId, string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId).AssertPeerFull();

        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
        var parsedChatPrincipalId = new ParsedChatPrincipalId(chatPrincipalId);
        if (!parsedChatPrincipalId.IsValid)
            return ChatAuthorRules.None(chatId);

        var userId = parsedChatPrincipalId.UserId.Id;
        var chatAuthor = (ChatAuthor)null!;
        if (userId.IsEmpty) {
            var chatAuthorId = parsedChatPrincipalId.AuthorId.Id;
            if (!chatAuthorId.IsEmpty)
                chatAuthor = await ChatAuthorsBackend
                    .Get(chatId, chatAuthorId, false, cancellationToken)
                    .ConfigureAwait(false);
            userId = chatAuthor?.UserId ?? default;
        }

        var otherUserId = (userId1, userId2).OtherThan(userId);
        if (userId.IsEmpty || otherUserId.IsEmpty) // One of these users should be chatPrincipalId
            return ChatAuthorRules.None(chatId);

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ChatAuthorRules.None(chatId);

        var otherAccount = await AccountsBackend.Get(otherUserId, cancellationToken).ConfigureAwait(false);
        if (otherAccount == null)
            return ChatAuthorRules.None(chatId);

        return new(chatId, chatAuthor, account, ChatPermissions.Write.AddImplied());
    }

    private async Task<Chat> GetOrCreatePeerChat(Symbol chatId, CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null)
            return chat;
        return await CreatePeerChat(chatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Chat> CreatePeerChat(Symbol chatId, CancellationToken cancellationToken)
    {
        _ = new ParsedChatId(chatId).AssertPeerFull();
        var cmd = new IChatsBackend.ChangeChatCommand(chatId, null, new() {
            Create = new ChatDiff() { ChatType = ChatType.Peer },
        });
        var chat = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        return chat!;
    }
}
