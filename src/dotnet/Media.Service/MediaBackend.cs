using ActualChat.Media.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Media;

public class MediaBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IMediaBackend
{
    private IDbEntityResolver<string, DbMedia> DbMediaResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbMedia>>();
    private IContentSaver ContentSaver { get; }
        = services.GetRequiredService<IContentSaver>();

    // [ComputeMethod]
    public virtual async Task<Media?> Get(MediaId mediaId, CancellationToken cancellationToken)
    {
        if (mediaId.IsNone)
            return null;

        var dbMedia = await DbMediaResolver.Get(mediaId, cancellationToken).ConfigureAwait(false);
        var media = dbMedia?.ToModel();
        return media;
    }

    // [ComputeMethod]
    public virtual async Task<Media?> GetByContentId(string contentId, CancellationToken cancellationToken)
    {
        if (contentId.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbMedia = await dbContext.Media
            .Where(x => x.ContentId == contentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbMedia?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Media?> OnChange(MediaBackend_Change command, CancellationToken cancellationToken)
    {
        var (mediaId, change) = command;
        if (Invalidation.IsActive) {
            if (!mediaId.IsNone)
                _ = Get(mediaId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
            if (dbMedia != null) {
                if (!dbMedia.ContentId.IsNullOrEmpty())
                    await ContentSaver.Remove(dbMedia.ContentId, cancellationToken)
                        .ConfigureAwait(false);

                dbContext.Remove(dbMedia);
            }
        }
        else
            throw new NotSupportedException("Update is not supported.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return media;
    }

    // [CommandHandler]
    public virtual async Task OnCopyChat(MediaBackend_CopyChat command, CancellationToken cancellationToken)
    {
        var (newChatId, correlationId, mediaIds) = command;
        if (mediaIds.Length == 0)
            return;

        var oldChatSid = mediaIds[0].Scope;
        if (Invalidation.IsActive)
            return;

        Log.LogInformation("-> OnCopyChat({CorrelationId}): MediaIds.Length={Count}",
            correlationId, mediaIds.Length);

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sids = mediaIds.Select(c => c.Value).ToList();
        var medias = await dbContext.Media
            .Where(c => c.Id.StartsWith(oldChatSid))
 #pragma warning disable CA1310
            .Where(c => sids.Any(x => c.Id == x))
 #pragma warning restore CA1310
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Log.LogInformation("OnCopyChat({CorrelationId}): {Count} medias found",
            correlationId, medias.Count);

        var foundMediaIds = new HashSet<string>(medias.Select(c => c.Id), StringComparer.Ordinal);
        var missingMediaIds = sids.Except(foundMediaIds, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal).ToList();
        var mistakenlyFoundMediaIds = foundMediaIds.Except(sids, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (missingMediaIds.Count > 0)
            Log.LogWarning("OnCopyChat({CorrelationId}) detected missing media ids ({Count}): {MediaIds}",
                correlationId, missingMediaIds.Count, missingMediaIds.ToCommaPhrase());
        if (mistakenlyFoundMediaIds.Count > 0)
            Log.LogWarning("OnCopyChat({CorrelationId}) detected mistakenly found media ids ({Count}): {MediaIds}",
                correlationId, mistakenlyFoundMediaIds.Count, mistakenlyFoundMediaIds.ToCommaPhrase());

        var newSids = mediaIds.Select(c => new MediaId(newChatId, c.LocalId).Value).ToList();
        var newChatSid = newChatId.Value;
        var existentMediaSids = await dbContext.Media
            .Where(c => c.Id.StartsWith(newChatSid))
 #pragma warning disable CA1310
            .Where(c => newSids.Any(x => c.Id == x))
 #pragma warning restore CA1310
            .Select(c => c.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existentMediaSidSet = new HashSet<string>(existentMediaSids, StringComparer.Ordinal);
        if (existentMediaSidSet.Count > 0)
            Log.LogWarning("OnCopyChat({CorrelationId}) detected existent media ids ({Count}): {MediaIds}",
            correlationId, existentMediaSidSet.Count, existentMediaSidSet.OrderBy(c => c, StringComparer.Ordinal) .ToCommaPhrase());

        var updateCount = 0;

        foreach (var dbMedia in medias) {
            var mediaId = new MediaId(newChatId, dbMedia.LocalId);
            if (existentMediaSidSet.Contains(mediaId.Value))
                continue;

            var contentId = dbMedia.ContentId;
            // Need to check if content is already exists because it can be second attempt to copy chat media
            // and content might be already copied in previous attempt that did not finish successfully.
            var newContentId = mediaId.ContentId(Path.GetExtension(dbMedia.ContentId));
            if (!await ContentSaver.Exists(newContentId, cancellationToken).ConfigureAwait(false))
                await ContentSaver.Copy(contentId, newContentId, cancellationToken).ConfigureAwait(false);
            else
                Log.LogInformation("Content with id '{NewContentId}' exists. Won't make a copy from content with id '{OriginalContentId}'",
                    newContentId, contentId);

            dbMedia.Id = mediaId.Value;
            dbMedia.Scope = mediaId.Scope;
            dbMedia.ContentId = newContentId;
            dbContext.Media.Add(dbMedia);
            updateCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Log.LogInformation("<- OnCopyChat({CorrelationId}) inserted {Count} media records",
            correlationId, updateCount);
    }
}
