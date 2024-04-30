using ActualChat.Search.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Search;

public class IndexedChatsBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), IIndexedChatsBackend
{
    private static IDbEntityResolver<string,DbIndexedChat>? _dbIndexedChatResolver;

    private IDbEntityResolver<string, DbIndexedChat> DbIndexedChatResolver =>
        _dbIndexedChatResolver ??= Services.DbEntityResolver<string, DbIndexedChat>();

    // [ComputeMethod]
    public virtual async Task<IndexedChat?> GetLast(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbIndexedChat = await dbContext.IndexedChats
            .Where(x => x.Id.StartsWith(DbIndexedChat.IdIndexSchemaVersionPrefix))
            .OrderByDescending(x => x.ChatCreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbIndexedChat?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<IndexedChat?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbIndexedChat = await DbIndexedChatResolver.Get(default, DbIndexedChat.ComposeId(chatId), cancellationToken).ConfigureAwait(false);
        return dbIndexedChat?.ToModel();
    }

    // Not a [ComputeMethod]!
    public async Task<ApiArray<IndexedChat>> List(
        Moment minCreatedAt,
        ChatId lastId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dMinCreatedAt = minCreatedAt.ToDateTime(DateTime.MinValue, DateTime.MaxValue);

        var dbChats = await dbContext.IndexedChats
            .Where(x => x.Id.StartsWith(DbIndexedChat.IdIndexSchemaVersionPrefix))
            .Where(x => x.ChatCreatedAt >= dMinCreatedAt)
            .OrderBy(x => x.ChatCreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbChats.Count == 0)
            return ApiArray<IndexedChat>.Empty;

        if (lastId.IsNone || dbChats[0].ChatCreatedAt > dMinCreatedAt)
            // no chats created at minCreatedAt that we need to skip
            return dbChats.Select(x => x.ToModel()).ToApiArray();

        var lastChatIdx = dbChats.FindIndex(x => x.GetChatId() == lastId);
        if (lastChatIdx < 0)
            return dbChats.Select(x => x.ToModel()).ToApiArray();

        return dbChats.Skip(lastChatIdx + 1).Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<IndexedChat?>> OnBulkChange(
        IndexedChatsBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        var changes = command.Changes;
        if (Invalidation.IsActive) {
            foreach (var chatId in changes.Select(x => x.Id).Distinct())
                _ = Get(chatId, default);
            if (changes.Any(x => x.Change.Kind == ChangeKind.Create))
                _ = GetLast(default);
            return default;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sidsUpdatedAndRemoved = changes.Where(x => x.Change.Kind is ChangeKind.Update or ChangeKind.Remove)
            .Select(x => DbIndexedChat.ComposeId(x.Id))
            .ToList();
        var existingDbIndexedChats = await dbContext.IndexedChats.Where(x => sidsUpdatedAndRemoved.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingDbIndexedChatMap = existingDbIndexedChats.ToDictionary(x => x.GetChatId());
        var dbResults = new List<DbIndexedChat?>(changes.Count);
        foreach (var change in changes)
            if (change.Change.IsCreate(out var create)) {
                var dbIndexedChat = new DbIndexedChat {
                    Id = DbIndexedChat.ComposeId(create.Id),
                    Version = VersionGenerator.NextVersion(),
                    ChatCreatedAt = create.ChatCreatedAt,
                };
                dbContext.Add(dbIndexedChat);
                dbResults.Add(dbIndexedChat);
            }
            else if (change.Change.IsUpdate(out var update)) {
                var dbIndexedChat = existingDbIndexedChatMap[change.Id];
                dbIndexedChat.RequireVersion(change.ExpectedVersion);
                update = update with { Version = VersionGenerator.NextVersion(dbIndexedChat.Version) };
                dbIndexedChat.UpdateFrom(update);
                dbContext.IndexedChats.Update(dbIndexedChat);
                dbResults.Add(dbIndexedChat);
            }
            else {
                dbContext.Remove(existingDbIndexedChatMap[change.Id]);
                dbResults.Add(null);
            }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbResults.Select(x => x?.ToModel()).ToApiArray();
    }
}
