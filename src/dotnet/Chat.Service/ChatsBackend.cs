using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Events;
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

    private readonly IAuthBackend _authBackend;
    private readonly IChatAuthors _chatAuthors;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IUserProfilesBackend _userProfilesBackend;
    private readonly IUserContactsBackend _userContactsBackend;
    private readonly IDbEntityResolver<string, DbChat> _dbChatResolver;
    private readonly IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef> _dbChatEntryIdGenerator;
    private readonly IEventPublisher _eventPublisher;

    public ChatsBackend(IServiceProvider services) : base(services)
    {
        _authBackend = Services.GetRequiredService<IAuthBackend>();
        _chatAuthors = Services.GetRequiredService<IChatAuthors>();
        _chatAuthorsBackend = Services.GetRequiredService<IChatAuthorsBackend>();
        _userProfilesBackend = Services.GetRequiredService<IUserProfilesBackend>();
        _userContactsBackend = services.GetRequiredService<IUserContactsBackend>();
        _dbChatResolver = Services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
        _dbChatEntryIdGenerator = Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        _eventPublisher = Services.GetRequiredService<IEventPublisher>();
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(string chatId, CancellationToken cancellationToken)
    {
        var dbChat = await _dbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<string[]> GetOwnedChatIds(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return Array.Empty<string>();

        string[] ownedChatIds;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            ownedChatIds = await dbContext.ChatOwners
                .Where(a => a.UserId == userId)
                .Select(a => a.ChatId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        return ownedChatIds;
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var chatPrincipalId = await _chatAuthors.GetChatPrincipalId(session, chatId, cancellationToken).ConfigureAwait(false);
        return await GetRules(chatId, chatPrincipalId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorRules> GetRules(
        string chatId,
        string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        var chatType = ChatId.GetChatType(chatId);
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null) {
            if (chatType is ChatType.Peer)
                return await GetDefaultPeerChatRules(chatId, chatPrincipalId, cancellationToken)
                    .ConfigureAwait(false);
            return new(null, null);
        }

        ParseChatPrincipalId(chatPrincipalId, out var authorId, out var userId);

        ChatAuthor? author = null;
        if (!authorId.IsNullOrEmpty()) {
            author = await _chatAuthorsBackend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
            userId = author?.UserId;
        }

        var user = !userId.IsNullOrEmpty()
            ? await _authBackend.GetUser(userId, cancellationToken).ConfigureAwait(false)
            : null;

        if (user != null && chat.OwnerIds.Contains(user.Id))
            return new(author, user, ChatPermissions.Owner);

        if (Constants.Chat.DefaultChatId == chatId) {
            var userProfile = user == null ? null
                : await _userProfilesBackend.Get(user.Id, cancellationToken).ConfigureAwait(false);
            return new(author, user, userProfile?.IsAdmin == true ? ChatPermissions.Owner : 0);
        }

        if (author != null)
            return new(author, user, ChatPermissions.ReadWrite);
        if (chat.IsPublic)
            return new(author, user, ChatPermissions.Read);
        return new(author, user);
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
    public virtual async Task<Chat> CreateChat(
        IChatsBackend.CreateChatCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Get(invChat.Id, default);
            foreach(var userIdInv in invChat.OwnerIds)
                _ = GetOwnedChatIds(userIdInv, default);
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        Symbol chatId = !command.Chat.Id.IsEmpty
            ? command.Chat.Id
            : ChatIdGenerator.Next(); // TODO: add reprocessing in case uniqueness conflicts
        var chat = command.Chat with {
            Id = chatId,
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
        };
        var dbChat = new DbChat(chat);
        dbContext.Add(dbChat);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Unit> UpdateChat(
        IChatsBackend.UpdateChatCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChat = context.Operation().Items.Get<Chat>()!;
            _ = Get(invChat.Id, default);
            return default;
        }

        var chat = command.Chat;
        var chatId = (string)chat.Id;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .SingleOrDefaultAsync(a => a.Id == chatId, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            throw new InvalidOperationException("chat does not exists");
        if (dbChat.Version != chat.Version)
            throw new InvalidOperationException("chat has been modified already");

        dbChat.Title = chat.Title;
        dbChat.Picture = chat.Picture;
        dbChat.IsPublic = chat.IsPublic;
        dbChat.Version = VersionGenerator.NextVersion();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return default;
    }

    /// <summary> The filter which creates chat for direct conversation between chat authors</summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    protected virtual async Task OnUpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var chatId = command.Entry.ChatId;
        var isPeerChatId = ChatId.IsPeerChatId(chatId);
        if (isPeerChatId)
            _ = await GetOrCreatePeerChat(chatId, cancellationToken).ConfigureAwait(false);

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (isPeerChatId)
            _ = EnsureContactsCreated(chatId, cancellationToken).ConfigureAwait(false); // no need to wait
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
            await _eventPublisher.Publish(chatEvent, cancellationToken).ConfigureAwait(false);
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

    // Protected methods

    protected void InvalidateChatPages(string chatId, ChatEntryType entryType, long entryId, bool isUpdate)
    {
        if (!isUpdate) {
            _ = GetEntryCount(chatId, entryType, null, false, default);
            _ = GetEntryCount(chatId, entryType, null, true, default);
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
            var compositeId = DbChatEntry.GetCompositeId(entry.ChatId, entryType, entry.Id);
            dbEntry = await dbContext.ChatEntries
                .FindAsync(DbKey.Compose(compositeId), cancellationToken)
                .ConfigureAwait(false)
                ?? throw new KeyNotFoundException();
            if (dbEntry.Version != entry.Version)
                throw new VersionMismatchException();
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
        => _dbChatEntryIdGenerator.Next(dbContext, new DbChatEntryShardRef(chatId, entryType), cancellationToken);

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

    private async Task<ChatAuthorRules> GetDefaultPeerChatRules(
        string chatId, string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        if (!ChatId.TryParseFullPeerChatId(chatId, out var userId1, out var userId2))
            return ChatAuthorRules.None;

        ParseChatPrincipalId(chatPrincipalId, out var chatAuthorId, out var userId);
        var chatAuthor = chatAuthorId.IsNullOrEmpty()
            ? null
            : await _chatAuthorsBackend.Get(chatId, chatAuthorId, false, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            userId = chatAuthor.UserId;

        if (!(Equals(userId, userId1) || Equals(userId, userId2))) // One of these users should be chatPrincipalId
            return ChatAuthorRules.None;
        var user = userId.IsNullOrEmpty()
            ? null
            : await _authBackend.GetUser(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return ChatAuthorRules.None;

        var otherUserId = user.Id == userId1 ? userId2 : userId1;
        var otherUser = await _authBackend.GetUser(otherUserId, cancellationToken).ConfigureAwait(false);
        if (otherUser == null)
            return ChatAuthorRules.None;

        return new(chatAuthor, user, ChatPermissions.ReadWrite);
    }

    private async Task EnsureContactsCreated(Symbol chatId, CancellationToken cancellationToken)
    {
        if (!ChatId.TryParseFullPeerChatId(chatId, out var userId1, out var userId2))
            return;
        await _userContactsBackend.GetOrCreate(userId1, userId2, cancellationToken).ConfigureAwait(false);
        await _userContactsBackend.GetOrCreate(userId2, userId1, cancellationToken).ConfigureAwait(false);
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
        var (userId1, userId2) = ChatId.ParseFullPeerChatId(chatId);
        var chat = new Chat {
            Id = chatId,
            OwnerIds = ImmutableArray<Symbol>.Empty.Add(userId1).Add(userId2),
            ChatType = ChatType.Peer
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        return await CreateChat(createChatCommand, cancellationToken).ConfigureAwait(false);
    }
}
