using ActualChat.Media.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Media;

internal class MediaBackend : DbServiceBase<MediaDbContext>, IMediaBackend
{
    private IDbEntityResolver<string, DbMedia> DbMediaResolver { get; }

    public MediaBackend(IServiceProvider services) : base(services)
        => DbMediaResolver = services.GetRequiredService<IDbEntityResolver<string, DbMedia>>();

    // [ComputeMethod]
    public virtual async Task<Media?> Get(MediaId mediaId, CancellationToken cancellationToken)
    {
        if (mediaId.IsNone)
            return null;

        var dbMedia = await DbMediaResolver.Get(mediaId, cancellationToken).ConfigureAwait(false);
        var media = dbMedia?.ToModel();
        return media;
    }

    // [CommandHandler]
    public virtual async Task<Media?> Change(IMediaBackend.ChangeCommand command, CancellationToken cancellationToken)
    {
        var (mediaId, change) = command;
        if (Computed.IsInvalidating()) {
            if (!mediaId.IsNone)
                _ = Get(mediaId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        if (change.IsCreate(out var media)) {
            var dbMedia = new DbMedia(media);
            dbContext.Media.Add(dbMedia);
        }
        else if (change.IsRemove()) {
            var dbMedia = await dbContext.Media
                .Get(mediaId, cancellationToken)
                .ConfigureAwait(false);
            media = dbMedia?.ToModel();
            if (dbMedia != null)
                dbContext.Remove(dbMedia);
        }
        else {
            throw new NotSupportedException();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return media;
    }
}
