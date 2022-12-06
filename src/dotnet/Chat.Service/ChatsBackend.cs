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
    private IContactsBackend ContactsBackend { get; }
    private IMarkupParser MarkupParser { get; }
    private IChatMentionResolverFactory ChatMentionResolverFactory { get; }
    private IDbEntityResolver<string, DbChat> DbChatResolver { get; }
    private IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> DbChatEntryIdGenerator { get; }
    private DiffEngine DiffEngine { get; }
    private HostInfo HostInfo { get; }

    public ChatsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        MarkupParser = services.GetRequiredService<IMarkupParser>();
        ChatMentionResolverFactory = services.GetRequiredService<BackendChatMentionResolverFactory>();
        DbChatResolver = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
        DbChatEntryIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
        HostInfo = services.GetRequiredService<HostInfo>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(chatId);

        var dbChat = await DbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorRules> GetRules(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsPeerChatId(out var peerChatId)) // We don't use actual roles to determine rules in this case
            return await GetPeerChatRules(peerChatId, principalId, cancellationToken).ConfigureAwait(false);

        // Group chat
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return AuthorRules.None(chatId);

        AuthorFull? author;
        AccountFull? account;
        if (principalId.IsUser(out var userId)) {
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return AuthorRules.None(chatId);

            author = await AuthorsBackend.GetByUserId(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        }
        else if (principalId.IsAuthor(out var authorId)) {
            author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return AuthorRules.None(chatId);

            account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return AuthorRules.None(chatId);
        }
        else
            return AuthorRules.None(chatId);

        var roles = ImmutableArray<Role>.Empty;
        var isJoined = author is { HasLeft: false };
        if (isJoined) {
            var isGuest = account.IsGuest;
            var isAnonymous = author is { IsAnonymous: true };
            roles = await RolesBackend
                .List(chatId, author!.Id, isGuest, isAnonymous, cancellationToken)
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
    public virtual async Task<ChatNews> GetNews(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return default;

        var idRange = await GetIdRange(chatId, ChatEntryKind.Text, false, cancellationToken).ConfigureAwait(false);
        var idTile = IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var tile = await GetTile(chatId, ChatEntryKind.Text, idTile.Range, false, cancellationToken).ConfigureAwait(false);
        var lastEntry = tile.Entries.Length > 0 ? tile.Entries[^1] : null;
        return new ChatNews(idRange, lastEntry);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Kind == entryKind);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => !e.IsRemoved);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbChatEntries = dbChatEntries
                .Where(e => e.LocalId >= idRangeValue.Start && e.LocalId < idRangeValue.End);
        }

        return await dbChatEntries.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        ChatId chatId,
        ChatEntryKind entryKind,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var minId = await GetMinId(chatId, entryKind, cancellationToken).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChatEntries = dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Kind == entryKind);
        if (!includeRemoved)
            dbChatEntries = dbChatEntries.Where(e => e.IsRemoved == false);
        var maxId = await dbChatEntries
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (minId, Math.Max(minId, maxId) + 1);
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(idTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = new List<ChatTile>();
            foreach (var smallerIdTile in smallerIdTiles) {
                var smallerChatTile = await GetTile(chatId, entryKind, smallerIdTile.Range, includeRemoved, cancellationToken)
                    .ConfigureAwait(false);
                smallerChatTiles.Add(smallerChatTile);
            }
            return new ChatTile(smallerChatTiles, includeRemoved);
        }
        if (!includeRemoved) {
            var fullTile = await GetTile(chatId, entryKind, idTileRange, true, cancellationToken).ConfigureAwait(false);
            return new ChatTile(idTileRange, false, fullTile.Entries.Where(e => !e.IsRemoved).ToImmutableArray());
        }

        // If we're here, it's the smallest tile & includeRemoved = true
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId
                && e.Kind == entryKind
                && e.LocalId >= idTile.Range.Start
                && e.LocalId < idTile.Range.End)
            .OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entryIdsWithAttachments = dbEntries.Where(x => x.HasAttachments)
            .Select(x => x.Id)
            .ToList();
        var allAttachments = entryIdsWithAttachments.Count > 0
            ? await dbContext.TextEntryAttachments
 #pragma warning disable MA0002
                .Where(x => entryIdsWithAttachments.Contains(x.EntryId))
 #pragma warning restore MA0002
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyCollection<DbTextEntryAttachment>)Array.Empty<DbTextEntryAttachment>();
        var attachmentsLookup = allAttachments.ToLookup(x => x.EntryId, StringComparer.Ordinal);
        var entries = dbEntries.Select(e => {
                var entryAttachments = attachmentsLookup[e.Id].Select(a => a.ToModel());
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
        var (chatId, expectedVersion, change, ownerId) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>();
            if (invChat != null)
                _ = Get(invChat.Id, default);
            return null!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        Chat chat;
        DbChat dbChat;
        if (change.IsCreate(out var update)) {
            var chatKind = update.Kind ?? chatId.Kind;
            if (chatKind == ChatKind.Group) {
                if (chatId.IsNone)
                    chatId = new ChatId(Generate.Option);
                else if (!Constants.Chat.SystemChatIds.Contains(chatId))
                    throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
            }
            else if (chatKind != ChatKind.Peer)
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid Change.Kind.");

            chat = new Chat(chatId) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            chat = ApplyDiff(chat, update);
            dbChat = new DbChat(chat);
            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (chatId.IsPeerChatId(out var peerChatId)) {
                // Peer chat
                ownerId.RequireNone();

                // Creating authors
                await peerChatId.UserIds
                    .ToArray()
                    .Select(userId => AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken))
                    .Collect(0)
                    .ConfigureAwait(false);

                // Creating contacts
                var (userId1, userId2) = peerChatId.UserIds;
                var contact1Task = ContactsBackend.GetOrCreateUserContact(userId1, userId2, cancellationToken);
                var contact2Task = ContactsBackend.GetOrCreateUserContact(userId2, userId1, cancellationToken);
                await Task.WhenAll(contact1Task, contact2Task).ConfigureAwait(false);
            }
            else if (chatId.Kind == ChatKind.Group) {
                // Group chat
                ownerId = ownerId.Require("Command.OwnerId");
                var author = await AuthorsBackend
                    .GetOrCreate(chatId, ownerId, cancellationToken)
                    .ConfigureAwait(false);

                var createOwnersRoleCmd = new IRolesBackend.ChangeCommand(chatId, default, null, new() {
                    Create = new RoleDiff() {
                        SystemRole = SystemRole.Owner,
                        Permissions = ChatPermissions.Owner,
                        AuthorIds = new SetDiff<ImmutableArray<AuthorId>, AuthorId>() {
                            AddedItems = ImmutableArray<AuthorId>.Empty.Add(author.Id),
                        },
                    },
                });
                await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);

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
            else
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
        }
        else if (change.IsUpdate(out update)) {
            ownerId.RequireNone();

            dbChat = await dbContext.Chats.ForUpdate()
                .SingleAsync(c => c.Id == chatId, cancellationToken)
                .ConfigureAwait(false);
            dbChat.RequireVersion(expectedVersion);
            chat = ApplyDiff(dbChat.ToModel(), update);
            dbChat.UpdateFrom(chat);
        }
        else
            throw StandardError.NotSupported("Chat removal is not supported yet.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;

        Chat ApplyDiff(Chat originalChat, ChatDiff? diff)
        {
            // Update
            var newChat = DiffEngine.Patch(originalChat, diff) with {
                Version = VersionGenerator.NextVersion(originalChat.Version),
            };
            if (newChat.Kind != originalChat.Kind)
                throw StandardError.Constraint("Chat kind cannot be changed.");

            // Validation
            switch (newChat.Kind) {
            case ChatKind.Group:
                if (newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Group chat title cannot be empty.");
                break;
            case ChatKind.Peer:
                if (!newChat.Title.IsNullOrEmpty())
                    throw StandardError.Constraint("Peer chat title must be empty.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), "Invalid chat kind.");
            }
            return newChat;
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> UpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        var entry = command.Entry;
        var changeKind = entry.LocalId == 0
            ? ChangeKind.Create
            : entry.IsRemoved ? ChangeKind.Remove : ChangeKind.Update;
        var chatId = entry.ChatId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            if (invChatEntry != null)
                InvalidateTiles(chatId, entry.Kind, invChatEntry.LocalId, changeKind);

            // Invalidate min-max Id range at last
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetIdRange(chatId, entry.Kind, true, default);
                _ = GetIdRange(chatId, entry.Kind, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetIdRange(chatId, entry.Kind, false, default);
                break;
            }
            return null!;
        }

        if (chatId.Kind == ChatKind.Peer)
            _ = await GetOrCreatePeerChat(chatId, cancellationToken).ConfigureAwait(false);

        // Injecting mention names into the markup
        if (entry.Kind == ChatEntryKind.Text && entry.Content.Length > 0) {
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

        if (entry.Kind is not ChatEntryKind.Text || entry.IsStreaming)
            return entry;

        // Let's enqueue the TextEntryChangedEvent
        var authorId = entry.AuthorId;
        var author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        var userId = author!.UserId;
        new TextEntryChangedEvent(entry, author, changeKind)
            .EnqueueOnCompletion(Queues.Users.ShardBy(userId), Queues.Chats.ShardBy(chatId));
        return entry;
    }

    // [CommandHandler]
    public virtual async Task<TextEntryAttachment> CreateAttachment(
        IChatsBackend.CreateAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var attachment = command.Attachment;
        var entryId = command.Attachment.EntryId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            InvalidateTiles(entryId.ChatId, entryId.EntryKind, entryId.LocalId, ChangeKind.Update);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatEntry = await dbContext.ChatEntries.Get(entryId, cancellationToken).Require().ConfigureAwait(false);
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
        if (Computed.IsInvalidating())
            return;

        await JoinAnnouncementsChat(@event.UserId, cancellationToken).ConfigureAwait(false);

        if (HostInfo.IsDevelopmentInstance)
            await JoinDefaultChatIfAdmin(@event.UserId, cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (author, _) = @event;
        if (author.ChatId == Constants.Chat.AnnouncementsChatId)
            return;

        var id = new ChatEntryId(author.ChatId, ChatEntryKind.Text, 0, AssumeValid.Option);
        var command = new IChatsBackend.UpsertEntryCommand(new ChatEntry(id) {
            AuthorId = Bots.GetWalleId(author.ChatId),
            ServiceEntry = new () {
                MembersChanged = new () {
                    AuthorId = author.Id,
                    HasLeft = author.HasLeft,
                },
            },
        });
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<long> GetMinId(
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId && e.Kind == entryKind)
            .OrderBy(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    protected void InvalidateTiles(ChatId chatId, ChatEntryKind entryKind, long entryId, ChangeKind changeKind)
    {
        // Invalidate global entry counts
        switch (changeKind) {
        case ChangeKind.Create:
            _ = GetEntryCount(chatId, entryKind, null, false, default);
            _ = GetEntryCount(chatId, entryKind, null, true, default);
            break;
        case ChangeKind.Remove:
            _ = GetEntryCount(chatId, entryKind, null, false, default);
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
                _ = GetTile(chatId, entryKind, idTile.Range, true, default);
            }
            switch (changeKind) {
            case ChangeKind.Create:
                _ = GetEntryCount(chatId, entryKind, idTile.Range, true, default);
                _ = GetEntryCount(chatId, entryKind, idTile.Range, false, default);
                break;
            case ChangeKind.Remove:
                _ = GetEntryCount(chatId, entryKind, idTile.Range, false, default);
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
        var entryId = entry.Id;
        var chatId = entry.ChatId;
        var entryKind = entry.Kind;
        var isNew = entryId.LocalId == 0;

        DbChatEntry dbEntry;
        if (isNew) {
            var localId = await DbNextLocalId(dbContext, entry.ChatId, entryKind, cancellationToken).ConfigureAwait(false);
            entryId = new ChatEntryId(chatId, entryKind, localId, AssumeValid.Option);
            entry = entry with {
                Id = entryId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
            };
            dbEntry = new (entry) {
                HasAttachments = hasAttachments,
            };

            dbContext.Add(dbEntry);
        }
        else {
            dbEntry = await dbContext.ChatEntries
                .Get(entryId, cancellationToken)
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

    private async Task JoinAnnouncementsChat(UserId userId, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        var author = await AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken).ConfigureAwait(false);

        if (!HostInfo.IsDevelopmentInstance)
            return;

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinDefaultChatIfAdmin(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { IsAdmin: true })
            return;

        var chatId = Constants.Chat.DefaultChatId;
        var author = await AuthorsBackend.GetOrCreate(chatId, userId, cancellationToken).ConfigureAwait(false);

        await AddOwner(chatId, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddOwner(ChatId chatId, Author author, CancellationToken cancellationToken)
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
                    AuthorIds = new SetDiff<ImmutableArray<AuthorId>, AuthorId> {
                        AddedItems = ImmutableArray<AuthorId>.Empty.Add(author.Id),
                    },
                },
            });
        await Commander.Call(createOwnersRoleCmd, cancellationToken).ConfigureAwait(false);
    }

    internal Task<long> DbNextLocalId(
        ChatDbContext dbContext,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
        => DbChatEntryIdGenerator.Next(dbContext, new DbChatEntryShardRef(chatId, entryKind), cancellationToken);

    private async Task<AuthorRules> GetPeerChatRules(
        PeerChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
    {
        AuthorFull? author = null;
        AccountFull? account = null;
        if (principalId.IsUser(out var userId))
            account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        else if (principalId.IsAuthor(out var authorId)) {
            author = await AuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return AuthorRules.None(chatId);

            account = await AccountsBackend.Get(author.UserId, cancellationToken).ConfigureAwait(false);
        }
        if (account == null)
            return AuthorRules.None(chatId);

        var otherUserId = chatId.UserIds.OtherThanOrDefault(account.Id);
        if (otherUserId.IsNone)
            return AuthorRules.None(chatId);

        return new(chatId, author, account, ChatPermissions.Write.AddImplied());
    }

    private async Task<Chat> GetOrCreatePeerChat(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Kind != ChatKind.Peer)
            throw new ArgumentOutOfRangeException(nameof(chatId), "Peer chat Id is expected here.");

        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat.IsStored())
            return chat;

        var command = new IChatsBackend.ChangeCommand(chatId, null, new() { Create = new ChatDiff() });
        chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return chat;
    }
}
