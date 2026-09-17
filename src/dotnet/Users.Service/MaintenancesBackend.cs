using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class MaintenancesBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IMaintenancesBackend
{
    // [ComputeMethod]
    public virtual async Task<MaintenanceMode> Get(MaintenanceKey key, CancellationToken cancellationToken)
    {
        key.RequireValid();
        // Deliberately isolated: depending on the partition would make every write invalidate every
        // key in it. OnSet invalidates this method for the key it actually changed instead.
        using var _ = Computed.BeginIsolation();
        var partition = await GetPartition(key.PartitionKey, cancellationToken).ConfigureAwait(false);
        return partition.GetValueOrDefault(key.Value);
    }

    // [CommandHandler]
    public virtual async Task OnSet(MaintenancesBackend_Set command, CancellationToken cancellationToken)
    {
        var (key, mode) = command;
        key.RequireValid();
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(command));

        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var id = key.ToString();
        await dbContext.Maintenances.Lock(id, cancellationToken).ConfigureAwait(false);
        var row = await dbContext.Maintenances
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (mode == MaintenanceMode.None) {
            if (row is not null)
                dbContext.Remove(row);
        }
        else if (row is null)
            dbContext.Add(new DbMaintenance { Id = id, Mode = mode });
        else
            row.Mode = mode;

        context.Operation.AddCompletionHandler(scope => {
            if (scope.IsCommitted != true)
                return Task.CompletedTask;

            using (Invalidation.Begin()) {
                _ = GetPartition(key.PartitionKey, default);
                _ = Get(key, default);
            }
            return Task.CompletedTask;
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    // Not exposed via IMaintenancesBackend: it must stay a local call of Get, which routes for it.
    [ComputeMethod(MinCacheDuration = 3600)]
    protected virtual async Task<ApiMap<string, MaintenanceMode>> GetPartition(
        ShardKey partitionKey,
        CancellationToken cancellationToken)
    {
        // Ties the cached partition to this node owning the shard: without this dependency a shard
        // that migrates away and comes back would be served from a cache, missed every write made on other nodes.
        ShardOwner.GetShardStateComputed(partitionKey.Head(MaintenanceKey.ShardKeySize), addDependency: true);

        var prefix = partitionKey.ToString();
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var dbContextLease = dbContext.ConfigureAwait(false);
        var rows = await dbContext.Maintenances.AsNoTracking()
            .Where(x => x.Id.StartsWith(prefix))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(x => x.Id[MaintenanceKey.IdPrefixLength..], x => x.Mode).ToApiMap();
    }
}
