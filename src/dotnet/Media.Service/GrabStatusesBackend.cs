using ActualChat.Db;
using ActualChat.Media.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class GrabStatusesBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), IGrabStatusesBackend
{
    private IDbEntityResolver<string, DbGrabStatus> DbGrabStatusResolver => field ??= Services.DbEntityResolver<string, DbGrabStatus>();

    // [ComputeMethod]
    public virtual async Task<GrabStatus?> Get(Symbol id, CancellationToken cancellationToken)
    {
        if (id.IsEmpty)
            return null;

        var dbGrabStatus = await DbGrabStatusResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbGrabStatus?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<GrabStatus> OnChange(GrabStatusesBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, success) = command;
        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbGrabStatus = await dbContext.GrabStatuses.Get(id, cancellationToken).ConfigureAwait(false);
        dbGrabStatus ??= new DbGrabStatus {
            Id = id,
        };
        dbGrabStatus.ModifiedAt = Clocks.SystemClock.Now;
        dbGrabStatus.Success = success;
        dbGrabStatus.Version = VersionGenerator.NextVersion(dbGrabStatus.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbGrabStatus.ToModel();
    }
}
