namespace ActualChat.Media;

public class Medias(IServiceProvider services) : IMedias
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaProgressBackend MediaProgressBackend { get; } = services.GetRequiredService<IMediaProgressBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<MediaProgress?> GetProgress(Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        if (!media.BlobId.IsNullOrEmpty())
            return new MediaProgress(mediaId, 0, MediaStage.Ready, 100, "");

        var mediaProgress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return mediaProgress;
    }

    // [ComputeMethod]
    public virtual async Task<MediaContent?> GetContent(Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        var contentId = media.BlobId;
        if (contentId.IsNullOrEmpty())
            return null;

        var thumbnailId = media.ThumbnailId;
        var thumbnailMedia = thumbnailId != null
            ? await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false)
            : null;

        var thumbnailContentId = thumbnailMedia?.BlobId;
        return new MediaContent(mediaId, contentId, thumbnailId, thumbnailContentId);
    }

    // [CommandHandler]
    public virtual async Task<MediaId> OnReserveMedia(Medias_ReserveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, scope) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var mediaId = MediaId.New(scope);
        var media = new MediaFull(mediaId) { UserId = account.Id, Metadata = command.Metadata };
        var mediaChange = new Change<MediaFull> { Create = media };

        await Commander.Call(new MediaBackend_Change(mediaId, null, mediaChange), cancellationToken).ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, MediaStage.Reserved, 0, "");
        var progressChange = new Change<MediaProgress> { Create = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, null, progressChange), cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    // [CommandHandler]
    public virtual async Task OnRemoveMedia(Medias_RemoveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, mediaId) = command;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var mediaChange = new Change<MediaFull> { Remove = true };
        await Commander.Call(new MediaBackend_Change(mediaId, null, mediaChange), cancellationToken).ConfigureAwait(false);

        var progressChange = new Change<MediaProgress> { Remove = true };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, null, progressChange), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdateProgress(Medias_UpdateProgress command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, mediaId, expectedVersion, stage, stageProgress, errorMessage) = command;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, stage, stageProgress, errorMessage);
        var change = new Change<MediaProgress> { Update = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, expectedVersion, change), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<MediaContent> OnProcessUpload(Medias_ProcessUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, mediaId, uploadId) = command;

        // Verify ownership
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        // Process upload and bind to media
        var mediaContent = await Commander.Call(new UploadsBackend_ProcessAndSaveContent(uploadId, mediaId), cancellationToken).ConfigureAwait(false);

        // Remove the upload
        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);

        return mediaContent;
    }

    // Private methods

    private async Task RequireOwner(Session session, MediaFull media, CancellationToken cancellationToken)
    {
        if (media.UserId == null)
            throw StandardError.Unauthorized("You don't have permission to access this media.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (media.UserId != account.Id)
            throw StandardError.Unauthorized("You don't have permission to access this media.");
    }
}
