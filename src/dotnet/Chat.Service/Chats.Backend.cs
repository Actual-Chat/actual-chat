using System.Security;
using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class Chats
{
    // [ComputeMethod]
    public virtual async Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbChat = await _dbChatResolver.Get(chatId, cancellationToken).ConfigureAwait(false);
        return dbChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        ChatId chatId,
        AuthorId? authorId,
        CancellationToken cancellationToken)
    {
        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return 0;

        var author = !(authorId?.IsNone ?? true)
            ? await _chatAuthorsBackend.Get(chatId, authorId.Value, false, cancellationToken).ConfigureAwait(false)
            : null;
        var user = author is { UserId.IsNone: false }
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
        ChatId chatId,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMessages = dbContext.ChatEntries.AsQueryable()
            .Where(m => m.ChatId == (string)chatId);

        if (idTileRange.HasValue) {
            var idRangeValue = idTileRange.GetValueOrDefault();
            IdTileStack.AssertIsTile(idRangeValue);
            dbMessages = dbMessages.Where(m =>
                m.Id >= idRangeValue.Start && m.Id < idRangeValue.End);
        }

        return await dbMessages.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    // Note that it returns (firstId, lastId + 1) range!
    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken)
    {
        var minId = await GetMinId(chatId, cancellationToken).ConfigureAwait(false);
        var maxId = await GetMaxId(chatId, cancellationToken).ConfigureAwait(false);
        return (minId, maxId + 1);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetMinId(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        return await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetMaxId(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        return await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value)
            .OrderByDescending(e => e.Id)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.GetTile(idTileRange);
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerChatTiles = new List<ChatTile>();
            foreach (var smallerIdTile in smallerIdTiles) {
                var smallerChatTile = await GetTile(chatId, smallerIdTile.Range, cancellationToken).ConfigureAwait(false);
                smallerChatTiles.Add(smallerChatTile);
            }
            return new ChatTile(smallerChatTiles);
        }

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEntries = await dbContext.ChatEntries
            .Where(m => m.ChatId == (string)chatId
                && m.Id >= idTile.Range.Start
                && m.Id < idTile.Range.End)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
        return new ChatTile(idTileRange, entries);
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> UpsertEntry(
        IChatsBackend.UpsertEntryCommand command,
        CancellationToken cancellationToken)
    {
        var chatEntry = command.Entry;
        var context = CommandContext.GetCurrent();
        var isUpdate = chatEntry.Id != 0;
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            if (invChatEntry != null)
                InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, isUpdate);
            _ = GetMaxId(chatEntry.ChatId, default); // We invalidate min-max Id range at last
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // TODO: Use entity resolver or remove this check (?)
        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleAsync(a => a.Id == (string)chatEntry.AuthorId, cancellationToken)
            .ConfigureAwait(false);
        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);

        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }

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

    // Protected methods

    protected async Task<Unit> AssertHasPermissions(
        ChatId chatId,
        AuthorId? authorId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await GetPermissions(chatId, authorId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");

        return default;
    }

    protected void InvalidateChatPages(ChatId chatId, long chatEntryId, bool isUpdate)
    {
        if (!isUpdate)
            _ = GetEntryCount(chatId, null, default);
        foreach (var idTile in IdTileStack.GetAllTiles(chatEntryId)) {
            _ = GetTile(chatId, idTile.Range, default);
            if (!isUpdate)
                _ = GetEntryCount(chatId, idTile.Range, default);
        }
    }

    protected async Task<DbChatEntry> DbAddOrUpdate(
        ChatDbContext dbContext,
        ChatEntry chatEntry,
        CancellationToken cancellationToken)
    {
        // AK: Suspicious - probably can lead to performance issues
        // AY: Yes, but the goal is to have a dense sequence here;
        //     later we'll change this to something that's more performant.
        var isNew = chatEntry.Id == 0;
        DbChatEntry dbChatEntry;
        if (isNew) {
            var dbChatId = (string)chatEntry.ChatId;
            var maxId = await dbContext.ChatEntries.ForUpdate() // To serialize inserts
                .Where(e => e.ChatId == dbChatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var id = await _idSequences.Next(dbChatId, maxId).ConfigureAwait(false);
            chatEntry = chatEntry with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
            };
            dbChatEntry = new (chatEntry);
            dbContext.Add(dbChatEntry);
        }
        else {
            var compositeId = DbChatEntry.GetCompositeId(chatEntry.ChatId, chatEntry.Id);
            dbChatEntry = await dbContext.ChatEntries.ForUpdate()
                    .FirstOrDefaultAsync(e => e.CompositeId == compositeId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(Invariant(
                    $"Chat entry with key {chatEntry.ChatId}, {chatEntry.Id} is not found"));
            chatEntry = chatEntry with {
                Version = VersionGenerator.NextVersion(dbChatEntry.Version),
            };
            dbChatEntry.UpdateFrom(chatEntry);
            dbContext.Update(dbChatEntry);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatEntry;
    }
}
