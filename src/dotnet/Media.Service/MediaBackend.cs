using ActualChat.Db;
using ActualChat.Media.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Media;

/// <summary>
/// Backend service implementation for managing media files (images, audio, video).
/// </summary>
public class MediaBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IMediaBackend
{
    private IDbEntityResolver<string, DbMedia> DbMediaResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbMedia>>();
    private IBlobStorage BlobStorage { get; }
        = services.BlobStorages()[BlobScope.ContentRecord];
    private ImageGrabber ImageGrabber => field ??= Services.GetRequiredService<ImageGrabber>();

    // [ComputeMethod]
    public virtual async Task<Media?> Get(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId == null)
            return null;

        var dbMedia = await DbMediaResolver.Get(mediaId.Value, cancellationToken).ConfigureAwait(false);
        var media = dbMedia?.ToModel().ToMedia();
        return media;
    }

    // [ComputeMethod]
    public virtual async Task<MediaFull?> GetFull(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId == null)
            return null;

        var dbMedia = await DbMediaResolver.Get(mediaId.Value, cancellationToken).ConfigureAwait(false);
        return dbMedia?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<Media?> GetByMediaIdScope(string mediaIdScope, CancellationToken cancellationToken)
    {
        if (mediaIdScope.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbMedia = await dbContext.Media.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id.StartsWith($"{mediaIdScope}{MediaId.Delimiter}"), cancellationToken)
            .ConfigureAwait(false);
        return dbMedia?.ToModel().ToMedia();
    }

    // [ComputeMethod]
    public virtual async Task<Media?> GetByBlobId(string blobId, CancellationToken cancellationToken)
    {
        if (blobId.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbMedia = await dbContext.Media
            .Where(x => x.BlobId == blobId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbMedia?.ToModel().ToMedia();
    }

    // [CommandHandler]
    public virtual async Task<MediaFull?> OnChange(MediaBackend_Change command, CancellationToken cancellationToken)
    {
        var (mediaId, expectedVersion, change) = command;
        change.RequireValid();
        // Seeded system media is shared by every chat that shows it
        if (change.IsRemove() && mediaId.IsSystem)
            return null;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        if (change.IsCreate(out var media)) {
            media = media with {
                Version = VersionGenerator.NextVersion()
            };
            var dbMedia = new DbMedia(media);
            dbContext.Media.Add(dbMedia);
        }
        else if (change.IsUpdate(out media)) {
            var dbMedia = await dbContext.Media
                .Get(mediaId.Value, cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
            media = media with {
                Version = VersionGenerator.NextVersion(dbMedia.Version)
            };
            dbMedia.UpdateFrom(media);
        }
        else if (change.IsRemove()) {
            var dbMedia = await dbContext.Media
                .Get(mediaId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbMedia is not null) {
                dbMedia.RequireVersion(expectedVersion);
                media = dbMedia.ToModel();
                if (!dbMedia.BlobId.IsNullOrEmpty())
                    await BlobStorage.Delete(dbMedia.BlobId, cancellationToken).ConfigureAwait(false);
                dbContext.Remove(dbMedia);
            }
        }
        else
            throw new NotSupportedException("Invalid change.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Invalidation.Defer(() => {
            _ = Get(mediaId, default);
            _ = GetFull(mediaId, default);
        });
        return media;
    }

    // [CommandHandler]
    public virtual async Task<MediaId?> OnGrabImage(MediaBackend_GrabImage command, CancellationToken cancellationToken)
    {
        try {
            return await ImageGrabber.GetOrGrab(command.Url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // A sender's bad URL must not fail the post; the caller drops the image
            Log.LogWarning(e, "GrabImage failed for {Url}", command.Url);
            return null;
        }
    }
}
