using System.Data;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Redis;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatService : DbServiceBase<ChatDbContext>, IChatService
{
    private readonly IDbEntityResolver<string, DbChat> _dbChatResolver;
    private readonly RedisSequenceSet<ChatService> _idSequences;

    public ChatService(
        IDbEntityResolver<string, DbChat> dbChatResolver,
        RedisSequenceSet<ChatService> idSequences,
        IServiceProvider services) : base(services)
    {
        _dbChatResolver = dbChatResolver;
        _idSequences = idSequences;
    }

    public virtual async Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbChat = await _dbChatResolver.TryGet(chatId, cancellationToken).ConfigureAwait(false);
        if (dbChat == null)
            return null;
        return dbChat.ToModel();
    }

    public virtual async Task<ChatPermissions> GetPermissions(
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var chat = await TryGet(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return 0;
        if (chat.OwnerIds.Contains(userId))
            return ChatPermissions.All;
        if (ChatConstants.DefaultChatId == chatId)
            return ChatPermissions.All;
        if (chat.IsPublic)
            return ChatPermissions.Read;
        return 0;
    }

    public virtual async Task<long> GetEntryCount(
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbMessages = dbContext.ChatEntries.AsQueryable()
            .Where(m => m.ChatId == (string)chatId);

        if (idRange.HasValue) {
            var idRangeValue = idRange.GetValueOrDefault();
            ChatConstants.IdTiles.AssertIsTile(idRangeValue);
            dbMessages = dbMessages.Where(m =>
                m.Id >= idRangeValue.Start && m.Id < idRangeValue.End);
        }

        return await dbMessages.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var __ = tx.ConfigureAwait(false);

        var firstId = await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value).OrderBy(e => e.Id).Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lastId = await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value).OrderByDescending(e => e.Id).Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (firstId, lastId);
    }

    public virtual async Task<ImmutableArray<ChatEntry>> GetEntries(
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        ChatConstants.IdTiles.AssertIsTile(idRange);
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEntries = await dbContext.ChatEntries
            .Where(m => m.ChatId == (string)chatId && m.Id >= idRange.Start && m.Id < idRange.End)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
        return entries;
    }

    protected void InvalidateChatPages(ChatId chatId, long chatEntryId, bool isUpdate)
    {
        if (!isUpdate)
            _ = GetEntryCount(chatId, null, default);
        foreach (var idRange in ChatConstants.IdTiles.GetCoveringTiles(chatEntryId)) {
            _ = GetEntries(chatId, idRange, default);
            if (!isUpdate)
                _ = GetEntryCount(chatId, idRange, default);
        }
    }
    // TODO: move the code inside commands (?)
    private async Task<DbChatEntry> DbAddOrUpdate(
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
                EndsAt = Clocks.SystemClock.Now,
            };
            dbChatEntry = new DbChatEntry(chatEntry);
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

    [CommandHandler, Internal]
    public virtual async Task<ChatEntry> CreateEntry(
        IChatService.CreateEntryCommand command,
        CancellationToken cancellationToken)
    {
        var chatEntry = command.Entry;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, isUpdate: false);
            _ = GetIdRange(chatEntry.ChatId, default); // We invalidate min-max Id range at last
            return null!;
        }
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        // TODO: use entity resolver or remove this check (?)
        var dbAuthor = await dbContext.Authors
            .FirstOrDefaultAsync(a => a.Id == (string)chatEntry.AuthorId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Exception(Invariant($"Can't find author with id: {chatEntry.AuthorId}"));

        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }

    // TODO: combine these two commands to upsert (?)
    [CommandHandler, Internal]
    public virtual async Task<ChatEntry> UpdateEntry(
        IChatService.UpdateEntryCommand command,
        CancellationToken cancellationToken)
    {
        var chatEntry = command.Entry;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invChatEntry = context.Operation().Items.Get<ChatEntry>();
            InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, isUpdate: true);
            // No need to invalidate GetIdRange here
            return null!;
        }
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        // TODO: use entity resolver or remove this check (?)
        var dbAuthor = await dbContext.Authors
            .FirstOrDefaultAsync(a => a.Id == (string)chatEntry.AuthorId, cancellationToken)
            .ConfigureAwait(false);

        var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken).ConfigureAwait(false);
        chatEntry = dbChatEntry.ToModel();
        context.Operation().Items.Set(chatEntry);
        return chatEntry;
    }

    [CommandHandler, Internal]
    public virtual async Task<Chat> CreateChat(IChatService.CreateChatCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return null!; // Nothing to invalidate

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChat = new DbChat(command.Chat with { Id = Ulid.NewUlid().ToString() }) {
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
        };
        dbContext.Add(dbChat);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var chat = dbChat.ToModel();
        context.Operation().Items.Set(chat);
        return chat;
    }
}
