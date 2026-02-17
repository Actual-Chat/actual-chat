using ActualChat.Db;
using ActualChat.Media.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class MediaProgressBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IMediaProgressBackend
{
    private IDbEntityResolver<string, DbMediaProgress> DbMediaProgressResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbMediaProgress>>();

    // [ComputeMethod]
    public virtual async Task<MediaProgress?> Get(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId == null)
            return null;

        var dbMediaProgress = await DbMediaProgressResolver.Get(mediaId.Value, cancellationToken).ConfigureAwait(false);
        return dbMediaProgress?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<MediaProgress?> OnChange(MediaProgressBackend_Change command, CancellationToken cancellationToken)
    {
        var (mediaId, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(mediaId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        MediaProgress? progress;
        if (change.IsCreate(out var createProgress)) {
            progress = createProgress with {
                Version = VersionGenerator.NextVersion()
            };
            var dbMediaProgress = new DbMediaProgress(progress);
            dbContext.MediaProgresses.Add(dbMediaProgress);
        }
        else if (change.IsUpdate(out var updateProgress)) {
            var dbMediaProgress = await dbContext.MediaProgresses
                .Get(mediaId.Value, cancellationToken)
                .ConfigureAwait(false);
            dbMediaProgress.RequireVersion(expectedVersion);
            progress = updateProgress with {
                Version = VersionGenerator.NextVersion(dbMediaProgress.Version)
            };
            dbMediaProgress.UpdateFrom(progress);
        }
        else if (change.IsRemove()) {
            var dbMediaProgress = await dbContext.MediaProgresses
                .Get(mediaId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbMediaProgress is not null) {
                dbMediaProgress.RequireVersion(expectedVersion);
                progress = dbMediaProgress.ToModel();
                dbContext.Remove(dbMediaProgress);
            }
            else
                progress = null;
        }
        else
            throw new NotSupportedException("Invalid change.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return progress;
    }
}
