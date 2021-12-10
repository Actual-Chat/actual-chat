using System.Security;
using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class Chats
{
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
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = dbContext.ChatEntries.AsQueryable()
            .Where(m => m.ChatId == chatId && m.Type == entryType);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbEntries = dbEntries
                .Where(m => m.Id >= idRangeValue.Start && m.Id < idRangeValue.End);
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
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(idTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = new List<ChatTile>();
            foreach (var smallerIdTile in smallerIdTiles) {
                var smallerChatTile = await GetTile(chatId, entryType, smallerIdTile.Range, cancellationToken)
                    .ConfigureAwait(false);
                smallerChatTiles.Add(smallerChatTile);
            }
            return new ChatTile(smallerChatTiles);
        }

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEntries = await dbContext.ChatEntries
            .Where(m => m.ChatId == chatId
                && m.Type == entryType
                && m.Id >= idTile.Range.Start
                && m.Id < idTile.Range.End)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
        return new ChatTile(idTileRange, entries);
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

        var chat = command.Chat with { Id = Ulid.NewUlid().ToString() };
        var dbChat = new DbChat(chat) {
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
        };
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
            _ = GetMaxId(entry.ChatId, entry.Type, default); // We invalidate min-max Id range at last
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // TODO: Use entity resolver or remove this check (?)
        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleAsync(a => a.Id == (string)entry.AuthorId, cancellationToken)
            .ConfigureAwait(false);
        var dbEntry = await DbAddOrUpdate(dbContext, entry, cancellationToken).ConfigureAwait(false);

        entry = dbEntry.ToModel();
        context.Operation().Items.Set(entry);
        return entry;
    }

    // [CommandHandler]
    public virtual async Task<(ChatEntry AudioEntry, ChatEntry TextEntry)> CreateAudioEntry(
        IChatsBackend.CreateAudioEntryCommand command,
        CancellationToken cancellationToken)
    {
        var audioEntry = command.AudioEntry;
        if (audioEntry.Type != ChatEntryType.Audio)
            throw new ArgumentOutOfRangeException(nameof(command), "AudioEntry.Type != ChatEntryType.Audio");

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            if (context.Operation().Items.TryGet<(ChatEntry AudioEntry, ChatEntry TextEntry)>(out var invEntries)) {
                var invAudioEntry = invEntries.AudioEntry;
                var invTextEntry = invEntries.TextEntry;
                InvalidateChatPages(invAudioEntry.ChatId, invAudioEntry.Type, invAudioEntry.Id, false);
                InvalidateChatPages(invTextEntry.ChatId, invAudioEntry.Type, invTextEntry.Id, false);
                _ = GetMaxId(invAudioEntry.ChatId, invAudioEntry.Type, default); // We invalidate min-max Id range at last
                _ = GetMaxId(invTextEntry.ChatId, invTextEntry.Type, default); // We invalidate min-max Id range at last
            }
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // TODO: Use entity resolver or remove this check (?)
        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleAsync(a => a.Id == (string)audioEntry.AuthorId, cancellationToken)
            .ConfigureAwait(false);

        var dbAudioEntry = await DbAddOrUpdate(dbContext, audioEntry, cancellationToken).ConfigureAwait(false);
        var textEntry = new ChatEntry() {
            ChatId = audioEntry.ChatId,
            AuthorId = audioEntry.AuthorId,
            Content = "...",
            Type = ChatEntryType.Text,
            StreamId = audioEntry.StreamId,
            AudioEntryId = dbAudioEntry.Id,
            BeginsAt = audioEntry.BeginsAt,
        };
        var dbTextEntry = await DbAddOrUpdate(dbContext, textEntry, cancellationToken).ConfigureAwait(false);

        audioEntry = dbAudioEntry.ToModel();
        textEntry = dbTextEntry.ToModel();
        var entries = (audioEntry, textEntry);
        context.Operation().Items.Set(entries);
        return entries;
    }

    // Protected methods

    protected async Task<Unit> AssertHasPermissions(
        string chatId,
        string? authorId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await GetPermissions(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");

        return default;
    }

    protected void InvalidateChatPages(string chatId, ChatEntryType entryType, long entryId, bool isUpdate)
    {
        if (!isUpdate)
            _ = GetEntryCount(chatId, entryType, null, default);
        foreach (var idTile in IdTileStack.GetAllTiles(entryId)) {
            _ = GetTile(chatId, entryType, idTile.Range, default);
            if (!isUpdate)
                _ = GetEntryCount(chatId, entryType, idTile.Range, default);
        }
    }

    protected async Task<DbChatEntry> DbAddOrUpdate(
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
            var dbChatId = entry.ChatId.Value;
            var maxId = await dbContext.ChatEntries.ForUpdate() // To serialize inserts
                .Where(e => e.ChatId == dbChatId && e.Type == entryType)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var idSequenceKey = $"{dbChatId}:{entryType:D}";
            var id = await _idSequences.Next(idSequenceKey, maxId).ConfigureAwait(false);
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
            dbEntry = await dbContext.ChatEntries.ForUpdate()
                    .FirstOrDefaultAsync(e => e.CompositeId == compositeId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(Invariant(
                    $"Chat entry with key {entry.ChatId}, {entry.Id} is not found"));
            entry = entry with {
                Version = VersionGenerator.NextVersion(dbEntry.Version),
            };
            dbEntry.UpdateFrom(entry);
            dbContext.Update(dbEntry);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbEntry;
    }
}
