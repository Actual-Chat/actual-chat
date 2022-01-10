using System.Security;
using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Versioning;

namespace ActualChat.Chat;

public partial class Chats
{
    private readonly ThreadSafeLruCache<Symbol, long> _maxIdCache = new(16384);

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(string chatId, CancellationToken cancellationToken)
    {
        var dbChat = await _dbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        string chatId,
        string? authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return 0;

        var author = !authorId.IsNullOrEmpty()
            ? await _chatAuthorsBackend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false)
            : null;
        var user = author is { UserId.IsEmpty: false }
            ? await _authBackend.GetUser(author.UserId, cancellationToken).ConfigureAwait(false)
            : null;

        if (user != null && chat.OwnerIds.Contains(user.Id))
            return ChatPermissions.All;
        if (Constants.Chat.DefaultChatId == chatId)
            return ChatPermissions.All;
        if (chat.IsPublic)
            return ChatPermissions.Read;

        return 0;
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

    // Command handlers

    // [CommandHandler]
    public virtual async Task<Chat> CreateChat(
        IChatsBackend.CreateChatCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return null!; // Nothing to invalidate

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chat = command.Chat with {
            Id = Ulid.NewUlid().ToString(),
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
        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleAsync(a => a.Id == entry.AuthorId.Value, cancellationToken)
            .ConfigureAwait(false);
        var isNew = entry.Id == 0;
        var dbEntry = await DbUpsertEntry(dbContext, entry, cancellationToken).ConfigureAwait(false);

        entry = dbEntry.ToModel();
        context.Operation().Items.Set(entry);
        context.Operation().Items.Set(isNew);
        return entry;
    }

    // Protected methods

    protected async Task AssertHasPermissions(
        string chatId,
        string? authorId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await GetPermissions(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");
    }

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
                .FindAsync(ComposeKey(compositeId), cancellationToken)
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

    internal async Task<long> DbNextEntryId(
        ChatDbContext dbContext,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
    {
        var idSequenceKey = new Symbol($"{chatId}:{entryType:D}");
        var maxId = _maxIdCache.GetValueOrDefault(idSequenceKey);
        if (maxId == 0) {
            _maxIdCache[idSequenceKey] = maxId =
                await dbContext.ChatEntries.ForUpdate() // To serialize inserts
                    .Where(e => e.ChatId == chatId && e.Type == entryType)
                    .OrderByDescending(e => e.Id)
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        var id = await _idSequences.Next(idSequenceKey, maxId).ConfigureAwait(false);
        _maxIdCache[idSequenceKey] = id;
        return id;
    }
}
