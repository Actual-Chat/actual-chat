using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

internal class RecentEntriesBackend : DbServiceBase<UsersDbContext>, IRecentEntriesBackend
{
    public RecentEntriesBackend(IServiceProvider services) : base(services) {}

    // [ComputeMethod]
    public virtual async Task<ImmutableHashSet<string>> List(
        string shardKey,
        RecentScope scope,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        await PseudoList(shardKey, scope).ConfigureAwait(false);

        var list = await dbContext.RecentEntries
            .Where(x => string.Equals(x.ShardKey, shardKey) && x.Scope == scope.ToString())
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => x.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list.ToImmutableHashSet();
    }

    // [CommandHandler]
    public virtual async Task<RecentEntry?> Update(IRecentEntriesBackend.UpdateCommand command, CancellationToken cancellationToken)
    {
        var (shardKey, scope, key, moment) = command;

        if (Computed.IsInvalidating()) {
            _ = PseudoList(command.ShardKey, command.Scope);
            return default;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbRecent = await dbContext.RecentEntries.Get(DbRecentEntry.GetId(shardKey, key), cancellationToken).ConfigureAwait(false);
        if (dbRecent != null) {
            dbRecent.UpdatedAt = moment;
            dbContext.Update(dbRecent);
        }
        else {
            dbRecent ??= new DbRecentEntry(new RecentEntry(shardKey, key, scope));
            dbContext.Add(dbRecent);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbRecent.ToModel();
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoList(string shardKey, RecentScope scope) => Stl.Async.TaskExt.UnitTask;
}
