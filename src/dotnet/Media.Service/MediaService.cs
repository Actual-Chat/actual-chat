namespace ActualChat.Media;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class MediaService(IServiceProvider services) : IMedia
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaProgressBackend MediaProgressBackend { get; } = services.GetRequiredService<IMediaProgressBackend>();
    private IUploadsBackend UploadsBackend { get; } = services.GetRequiredService<IUploadsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<MediaProgress?> GetProgress(Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        if (!media.BlobId.IsNullOrEmpty())
            return new MediaProgress(mediaId, 0, MediaProcessingStage.Ready, 100);

        var progress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return progress;
    }

    // [ComputeMethod]
    public virtual async Task<MediaRef?> GetContent(Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        var blobId = media.BlobId;
        if (blobId.IsNullOrEmpty())
            return null;

        var thumbnailId = media.ThumbnailId;
        var thumbnailMedia = thumbnailId != null
            ? await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false)
            : null;

        var thumbnailBlobId = thumbnailMedia?.BlobId;
        return new MediaRef(mediaId, blobId, thumbnailId, thumbnailBlobId);
    }

    // [CommandHandler]
    public virtual async Task<MediaId> OnReserveMedia(Media_ReserveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, scope) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var mediaId = MediaId.New(scope);
        var media = new MediaFull(mediaId) { UserId = account.Id, Kind = command.Kind, Metadata = command.Metadata };
        var mediaChange = new Change<MediaFull> { Create = media };

        await Commander.Call(new MediaBackend_Change(mediaId, null, mediaChange), cancellationToken).ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, MediaProcessingStage.Reserved, 0);
        var progressChange = new Change<MediaProgress> { Create = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, null, progressChange), cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    // [CommandHandler]
    public virtual async Task OnRemoveMedia(Media_RemoveMedia command, CancellationToken cancellationToken)
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
    public virtual async Task OnUpdateProgress(Media_UpdateProgress command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, mediaId, expectedVersion, stage, stageProgress, error) = command;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, stage, stageProgress, error);
        var change = new Change<MediaProgress> { Update = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, expectedVersion, change), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<MediaRef> OnProcessUpload(Media_ProcessUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, mediaId, uploadId) = command;

        // Verify ownership
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        var upload = await UploadsBackend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null || upload.UserId != media.UserId)
            throw StandardError.Upload.NotFound();

        // Process upload and bind to media
        var mediaRef = await Commander
            .Call(new UploadsBackend_ProcessAndSaveContent(uploadId, mediaId), cancellationToken)
            .ConfigureAwait(false);

        // Remove the upload
        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);

        return mediaRef;
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
