using System.ComponentModel.DataAnnotations;
using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Commands;
using ActualChat.Contacts;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatsBackend : DbServiceBase<ChatDbContext>, IChatsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private IMarkupParser MarkupParser { get; }
    private IChatMentionResolverFactory ChatMentionResolverFactory { get; }
    private IContactsBackend ContactsBackend { get; }
    private IDbEntityResolver<string, DbChat> DbChatResolver { get; }
    private IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> DbChatEntryIdGenerator { get; }
    private DiffEngine DiffEngine { get; }
    private HostInfo HostInfo { get; }

    public ChatsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        MarkupParser = services.GetRequiredService<IMarkupParser>();
        ChatMentionResolverFactory = services.GetRequiredService<BackendChatMentionResolverFactory>();
        DbChatResolver = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
        DbChatEntryIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
        HostInfo = services.GetRequiredService<HostInfo>();
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
    public virtual async Task<AuthorRules> GetRules(
        string chatId,
        string principalId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        var parsedPrincipalId = new ParsedPrincipalId(principalId);
        if (!parsedChatId.IsValid || !parsedPrincipalId.IsValidOrEmpty)
            return AuthorRules.None(chatId);
        var (parsedAuthorId, parsedUserId) = parsedPrincipalId;

        // Peer chat: we don't use actual roles to determine rules here
        var chatType = parsedChatId.Kind.ToChatType();
        if (chatType is ChatType.Peer)
            return await GetPeerChatRules(chatId, principalId, cancellationToken).ConfigureAwait(false);

        // Group chat
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return AuthorRules.None(chatId);

        AuthorFull? author = null;
        if (parsedPrincipalId.Kind == PrincipalKind.Author) {
            author = await AuthorsBackend.Get(chatId, parsedAuthorId, cancellationToken).ConfigureAwait(false);
            parsedUserId = author?.UserId ?? Symbol.Empty;
        } // Otherwise parsedUserId is either valid or empty
        var account = parsedUserId.IsValid ? null
            : await AccountsBackend.Get(parsedUserId, cancellationToken).ConfigureAwait(false);

        var roles = ImmutableArray<Role>.Empty;
        var isJoined = author is { HasLeft: false };
        if (isJoined) {
            var isAuthenticated = account != null;
            var isAnonymous = author is { IsAnonymous: true };
            roles = await RolesBackend
                .List(chatId, author!.Id, isAuthenticated, isAnonymous, cancellationToken)
                .ConfigureAwait(false);
        }
        var permissions = roles.ToPermissions();
        if (chat.IsPublic) {
            permissions |= ChatPermissions.Join;
            if (!isJoined) {
                var anyoneSystemRole = await RolesBackend.GetSystem(chatId, SystemRole.Anyone, cancellationToken).ConfigureAwait(false);
                if (anyoneSystemRole != null && anyoneSystemRole.Permissions.Has(ChatPermissions.SeeMembers))
                    permissions |= ChatPermissions.SeeMembers;
            }
        }
        permissions = permissions.AddImplied();

        var rules = new AuthorRules(chatId, author, account, permissions);
        return rules;
    }

    // [ComputeMethod]
    public virtual async Task<ChatSummary?> GetSummary(
        string chatId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        if (!parsedChatId.IsValid)
            return null;

        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var idRange = await GetIdRange(chatId, ChatEntryType.Text, false, cancellationToken).ConfigureAwait(false);
        var idTile = IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var tile = await GetTile(chatId, ChatEntryType.Text, idTile.Range, false, cancellationToken).ConfigureAwait(false);
        var lastEntry = tile.Entries.Length > 0 ? tile.Entries[^1] : null;
        return new ChatSummary() {
            TextEntryIdRange = idRange,
            LastTextEntry = lastEntry,
        };
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

        var dbChatEntries = dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Type == entryType);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => !e.IsRemoved);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbChatEntries = dbChatEntries
                .Where(e => e.Id >= idRangeValue.Start && e.Id < idRangeValue.End);
        }

        return await dbChatEntries.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        string chatId,
        ChatEntryType entryType,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var minId = await GetMinId(chatId, entryType, cancellationToken).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Type == entryType);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => e.IsRemoved == false);
        var maxId = await dbChatEntries
            .OrderByDescending(e => e.Id)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (minId, Math.Max(minId, maxId) + 1);
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
        var entryIdsWithAttachments = dbEntries.Where(x => x.HasAttachments)
            .Select(x => x.CompositeId)
            .ToList();
        var allAttachments = entryIdsWithAttachments.Count > 0
            ? await dbContext.TextEntryAttachments
 #pragma warning disable MA0002
                .Where(x => entryIdsWithAttachments.Contains(x.ChatEntryId))
 #pragma warning restore MA0002
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyCollection<DbTextEntryAttachment>)Array.Empty<DbTextEntryAttachment>();
        var attachmentsLookup = allAttachments.ToLookup(x => x.ChatEntryId, StringComparer.Ordinal);
        var entries = dbEntries.Select(e => {
                var entryAttachments = attachmentsLookup[e.CompositeId].Select(a => a.ToModel());
                return e.ToModel(entryAttachments);
            })
            .ToImmutableArray();
        return new ChatTile(idTileRange, true, entries);
    }

    // Command handlers

    // [CommandHandler]
    public virtual async Task<Chat> Change(
        IChatsBackend.ChangeCommand command,
        CancellationToken cancellationToken)
    {
        var (chatId, expectedVersion, change, creatorUserId) = command;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Get(invChat.Id, default);
            return null!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        Chat chat;
        DbChat dbChat;
        if (change.IsCreate(out var update)) {
            if (!Constants.Chat.PredefinedChatIds.Contains(chatId)) {
                chatId.RequireEmpty("command.ChatId");
                chatId = DbChat.IdGenerator.Next();
            }
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
            parsedChatId = isPeer ? parsedChatId.RequirePeerFullChatId() : parsedChatId.RequireGroupChatId();

            dbChat = new DbChat(chat);
            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (chat.ChatType is ChatType.Peer) {
                // Peer chat
                creatorUserId.RequireEmpty("command.CreatorUserId");
                var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
                var ownerUserIds = new[] { userId1.Value, userId2.Value };
                await ownerUserIds
                    .Select(userId => AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken))
                    .Collect(0)
                    .ConfigureAwait(false);
                var contact1Task = ContactsBackend.GetOrCreateUserContact(userId1, userId2, cancellationToken);
                var contact2Task = ContactsBackend.GetOrCreateUserContact(userId2, userId1, cancellationToken);
                await Task.WhenAll(contact1Task, contact2Task).ConfigureAwait(false);
            }
            else {
                // Group chat
                creatorUserId = creatorUserId.RequireNonEmpty("Command.CreatorUserId");
                var author = await AuthorsBackend
                    .GetOrCreate(chatId, creatorUserId, cancellationToken)
                    .ConfigureAwait(false);

                var createOwnersRoleCmd = new IRolesBackend.ChangeCommand(chatId, "", null, new() {
                    Create = new RoleDiff() {
                        SystemRole = SystemRole.Owner,
                        Permissions = ChatPermissions.Owner,
                        AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol>() {
                            AddedItems = ImmutableArray<Symbol>.Empty.Add(author.Id),
                        },
                    },
                });
                await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);

                var createJoinedRoleCmd = new IRolesBackend.ChangeCommand(chatId, "", null, new() {
                    Create = new RoleDiff() {
                        SystemRole = SystemRole.Anyone,
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
            dbChat = await dbContext.Chats
                .Get(chatId, cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
            chat = dbChat.ToModel();
            if ((update.ChatType ?? chat.ChatType) != chat.ChatType)
                throw StandardError.Constraint("Chat type cannot be changed.");

            chat = chat with {
                Version = VersionGenerator.NextVersion(chat.Version),
            };
            chat = DiffEngine.Patch(chat, update);
            if (chat.ChatType is not ChatType.Peer && chat.Title.IsNullOrEmpty())
                throw new ValidationException("Chat title cannot be empty.");
            dbChat.UpdateFrom(chat);
        }
        else
            throw StandardError.NotSupported("Chat removal is not supported yet.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> UpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        var entry = command.Entry;
        var changeKind = entry.Id == 0 ? ChangeKind.Create : entry.IsRemoved ? ChangeKind.Remove : ChangeKind.Update;
        var chatId = entry.ChatId;

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            if (invChatEntry != null)
                InvalidateTiles(chatId, entry.Type, invChatEntry.Id, changeKind);

            // Invalidate min-max Id range at last
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetIdRange(chatId, entry.Type, true, default);
                _ = GetIdRange(chatId, entry.Type, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetIdRange(chatId, entry.Type, false, default);
                break;
            }
            return null!;
        }

        var parsedChatId = new ParsedChatId(chatId).RequireValid();
        var isPeer = parsedChatId.Kind.IsPeerAny();
        if (isPeer)
            _ = await GetOrCreatePeerChat(chatId, cancellationToken).ConfigureAwait(false);

        // Injecting mention names into the markup
        if (entry.Type == ChatEntryType.Text && entry.Content.Length > 0) {
            var content = entry.Content;
            var markup = MarkupParser.Parse(content);
            var mentionNamer = new MentionNamer(ChatMentionResolverFactory.Create(chatId));
            markup = await mentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
            content = MarkupFormatter.Default.Format(markup);
            entry = entry with { Content = content };
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntry = await DbUpsertEntry(dbContext, entry, command.HasAttachments, cancellationToken).ConfigureAwait(false);
        entry = dbEntry.ToModel();
        context.Operation().Items.Set(entry);

        if (entry.Type is not ChatEntryType.Text || entry.IsStreaming)
            return entry;

        // Let's enqueue the TextEntryChangedEvent
        var authorId = entry.AuthorId;
        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        var userId = author!.UserId;
        new TextEntryChangedEvent(chatId, entry.Id, authorId, entry.Content, changeKind)
            .EnqueueOnCompletion(Queues.Chats.ShardBy(chatId), Queues.Users.ShardBy(userId));
        return entry;
    }

    // [CommandHandler]
    public virtual async Task<TextEntryAttachment> CreateAttachment(
        IChatsBackend.CreateAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var attachment = command.Attachment;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            InvalidateTiles(command.Attachment.ChatId, ChatEntryType.Text, command.Attachment.EntryId, ChangeKind.Update);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var compositeId = DbChatEntry.ComposeId(attachment.ChatId, ChatEntryType.Text, attachment.EntryId);
        var dbChatEntry = await dbContext.ChatEntries.Get(compositeId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<ChatEntry>();
        if (dbChatEntry.IsRemoved)
            throw StandardError.Constraint("Removed chat entries cannot be modified.");

        attachment = attachment with {
            Version = VersionGenerator.NextVersion(),
        };
        var dbAttachment = new DbTextEntryAttachment(attachment);
        dbContext.Add(dbAttachment);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        attachment = dbAttachment.ToModel();
        context.Operation().Items.Set(attachment);
        return attachment;
    }

    // Event handlers

    [EventHandler]
    public virtual async Task OnNewUserEvent(NewUserEvent @event, CancellationToken cancellationToken)
    {
        await JoinAnnouncementsChat(@event.UserId, cancellationToken).ConfigureAwait(false);

        if (HostInfo.IsDevelopmentInstance)
            await JoinDefaultChatIfAdmin(@event.UserId, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<long> GetMinId(
        string chatId,
        ChatEntryType entryType,
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

    protected void InvalidateTiles(string chatId, ChatEntryType entryType, long entryId, ChangeKind changeKind)
    {
        // Invalidate global entry counts
        switch (changeKind) {
        case ChangeKind.Create:
            _ = GetEntryCount(chatId, entryType, null, false, default);
            _ = GetEntryCount(chatId, entryType, null, true, default);
            break;
        case ChangeKind.Remove:
            _ = GetEntryCount(chatId, entryType, null, false, default);
            break;
        }

        // Invalidate GetTile & GetEntryCount for chat tiles
        foreach (var idTile in IdTileStack.GetAllTiles(entryId)) {
            if (idTile.Layer.Smaller == null) {
                // Larger tiles are composed out of smaller tiles,
                // so we have to invalidate just the smallest one.
                // And the tile with includeRemoved == false is based on
                // a tile with includeRemoved == true, so we have to invalidate
                // just this tile.
                _ = GetTile(chatId, entryType, idTile.Range, true, default);
            }
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetEntryCount(chatId, entryType, idTile.Range, true, default);
                _ = GetEntryCount(chatId, entryType, idTile.Range, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetEntryCount(chatId, entryType, idTile.Range, false, default);
                break;
            }
        }
    }

    protected async Task<DbChatEntry> DbUpsertEntry(
        ChatDbContext dbContext,
        ChatEntry entry,
        bool hasAttachments,
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
            dbEntry = new (entry) {
                HasAttachments = hasAttachments,
            };

            dbContext.Add(dbEntry);
        }
        else {
            var compositeId = DbChatEntry.ComposeId(entry.ChatId, entryType, entry.Id);
            dbEntry = await dbContext.ChatEntries
                .Get(compositeId, cancellationToken)
                .RequireVersion(entry.Version)
                .ConfigureAwait(false)
                ?? throw StandardError.NotFound<ChatEntry>();
            if (dbEntry.IsRemoved)
                throw StandardError.Constraint("Removed chat entries cannot be modified.");
            entry = entry with {
                Version = VersionGenerator.NextVersion(dbEntry.Version),
            };
            dbEntry.UpdateFrom(entry);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbEntry;
    }

    // Private / internal methods

    private async Task JoinAnnouncementsChat(string userId, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        var author = await AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken).ConfigureAwait(false);

        if (!HostInfo.IsDevelopmentInstance)
            return;

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null || !account.IsAdmin)
            return;

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinDefaultChatIfAdmin(string userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null || !account.IsAdmin)
            return;

        var chatId = Constants.Chat.DefaultChatId;
        var author = await AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken).ConfigureAwait(false);

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddOwner(string chatId, Author author, CancellationToken cancellationToken)
    {
        var ownerRole = await RolesBackend.GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false);
        if (ownerRole == null)
            return;

        var createOwnersRoleCmd = new IRolesBackend.ChangeCommand(chatId,
            ownerRole.Id,
            null,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<ImmutableArray<Symbol>, Symbol> {
                        AddedItems = ImmutableArray<Symbol>.Empty.Add(author.Id),
                    },
                },
            });
        await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
    }

    internal Task<long> DbNextEntryId(
        ChatDbContext dbContext,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => DbChatEntryIdGenerator.Next(dbContext, new DbChatEntryShardRef(chatId, entryType), cancellationToken);

    private async Task<AuthorRules> GetPeerChatRules(
        string chatId, string principalId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        var parsedPrincipalId = new ParsedPrincipalId(principalId);
        if (parsedChatId.Kind != ChatIdKind.PeerFull || !parsedPrincipalId.IsValid)
            return AuthorRules.None(chatId);

        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
        var userId = parsedPrincipalId.UserId.Id;
        var author = (AuthorFull)null!;
        if (userId.IsEmpty) {
            var authorId = parsedPrincipalId.AuthorId.Id;
            if (!authorId.IsEmpty)
                author = await AuthorsBackend
                    .Get(chatId, authorId, cancellationToken)
                    .ConfigureAwait(false);
            userId = author?.UserId ?? Symbol.Empty;
        }

        var otherUserId = (userId1, userId2).OtherThan(userId);
        if (userId.IsEmpty || otherUserId.IsEmpty) // One of these users should be principalId
            return AuthorRules.None(chatId);

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return AuthorRules.None(chatId);

        var otherAccount = await AccountsBackend.Get(otherUserId, cancellationToken).ConfigureAwait(false);
        if (otherAccount == null)
            return AuthorRules.None(chatId);

        return new(chatId, author, account, ChatPermissions.Write.AddImplied());
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
        _ = new ParsedChatId(chatId).RequirePeerFullChatId();
        var command = new IChatsBackend.ChangeCommand(chatId, null, new() {
            Create = new ChatDiff() { ChatType = ChatType.Peer },
        });
        var chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return chat!;
    }
}
