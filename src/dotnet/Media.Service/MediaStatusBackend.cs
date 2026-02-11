using ActualChat.Db;
using ActualChat.Media.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class MediaStatusBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IMediaStatusBackend
{
    private IDbEntityResolver<string, DbMediaStatus> DbMediaStatusResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbMediaStatus>>();

    // [ComputeMethod]
    public virtual async Task<MediaStatusInfo?> Get(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId == null)
            return null;

        var dbMediaStatus = await DbMediaStatusResolver.Get(mediaId.Value, cancellationToken).ConfigureAwait(false);
        return dbMediaStatus?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<MediaStatusInfo?> OnChange(MediaStatusBackend_Change command, CancellationToken cancellationToken)
    {
        var (mediaId, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(mediaId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        MediaStatusInfo? statusInfo;
        if (change.IsCreate(out var createStatus)) {
            statusInfo = createStatus with {
                Version = VersionGenerator.NextVersion()
            };
            var dbMediaStatus = new DbMediaStatus(statusInfo);
            dbContext.MediaStatuses.Add(dbMediaStatus);
        }
        else if (change.IsUpdate(out var updateStatus)) {
            var dbMediaStatus = await dbContext.MediaStatuses
                .Get(mediaId.Value, cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
            statusInfo = updateStatus with {
                Version = VersionGenerator.NextVersion(dbMediaStatus.Version)
            };
            dbMediaStatus.UpdateFrom(statusInfo);
        }
        else if (change.IsRemove()) {
            var dbMediaStatus = await dbContext.MediaStatuses
                .Get(mediaId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbMediaStatus is not null) {
                dbMediaStatus.RequireVersion(expectedVersion);
                statusInfo = dbMediaStatus.ToModel();
                dbContext.Remove(dbMediaStatus);
            }
            else
                statusInfo = null;
        }
        else
            throw new NotSupportedException("Invalid change.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return statusInfo;
    }
}
