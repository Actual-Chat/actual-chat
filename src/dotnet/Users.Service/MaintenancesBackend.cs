using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class MaintenancesBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IMaintenancesBackend
{
    // [ComputeMethod]
    public virtual async Task<MaintenanceMode> GetMode(MaintenanceKey key, CancellationToken cancellationToken)
    {
        key.RequireValid();
        // Deliberately isolated: depending on the partition would make every write invalidate every
        // key in it. OnSet invalidates this method for the key it actually changed instead.
        using var _ = Computed.BeginIsolation();
        var partition = await GetPartition(key.PartitionKey, cancellationToken).ConfigureAwait(false);
        return partition.GetValueOrDefault(key.Value)?.Mode ?? MaintenanceMode.None;
    }

    // [ComputeMethod]
    public virtual async Task<Maintenance> Get(MaintenanceKey key, CancellationToken cancellationToken)
    {
        key.RequireValid();
        // Isolated for the same reason as GetMode
        using var _ = Computed.BeginIsolation();
        var partition = await GetPartition(key.PartitionKey, cancellationToken).ConfigureAwait(false);
        return partition.GetValueOrDefault(key.Value) ?? Maintenance.None;
    }

    // [CommandHandler]
    public virtual async Task OnSet(MaintenancesBackend_Set command, CancellationToken cancellationToken)
    {
        var (key, mode) = command;
        key.RequireValid();
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(command));

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var id = key.ToString();
        await dbContext.Maintenances.Lock(id, cancellationToken).ConfigureAwait(false);
        var row = await dbContext.Maintenances
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is not null && row.OwnerId != command.OwnerId)
            throw StandardError.Constraint("Maintenance belongs to another operation.");

        var targets = command.Targets.ToDelimitedString(" ");
        if (mode == MaintenanceMode.None) {
            if (row is not null)
                dbContext.Remove(row);
        }
        else if (row is null)
            dbContext.Add(new DbMaintenance {
                Id = id,
                Mode = mode,
                OwnerId = command.OwnerId,
                StartedBy = command.StartedBy?.Value ?? "",
                StartedAt = command.StartedAt ?? Clocks.SystemClock.Now,
                Targets = targets,
            });
        else {
            row.Mode = mode;
            row.Targets = targets;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => {
            _ = GetPartition(key.PartitionKey, default);
            _ = GetMode(key, default);
            _ = Get(key, default);
        });
    }

    // Protected methods

    // Not exposed via IMaintenancesBackend: it must stay a local call of Get, which routes for it.
    [ComputeMethod(MinCacheDuration = 3600)]
    protected virtual async Task<ApiMap<string, Maintenance>> GetPartition(
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
        return rows
            .ToDictionary(x => x.Id[MaintenanceKey.IdPrefixLength..], x => x.ToModel())
            .ToApiMap();
    }
}
