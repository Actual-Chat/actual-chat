namespace ActualChat.Media;

public class Medias(IServiceProvider services) : IMedias
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaStatusBackend MediaStatusBackend { get; } = services.GetRequiredService<IMediaStatusBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<MediaStatusInfo?> GetStatus(Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        if (!media.ContentId.IsNullOrEmpty())
            return new MediaStatusInfo(mediaId, MediaStatus.Ready);

        return await MediaStatusBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
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

        await Commander.Call(new MediaBackend_Change(mediaId, mediaChange), cancellationToken).ConfigureAwait(false);

        var statusInfo = new MediaStatusInfo(mediaId, MediaStatus.Reserved);
        var statusChange = new Change<MediaStatusInfo> { Create = statusInfo };
        await Commander.Call(new MediaStatusBackend_Change(mediaId, statusChange), cancellationToken).ConfigureAwait(false);

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
        await Commander.Call(new MediaBackend_Change(mediaId, mediaChange), cancellationToken).ConfigureAwait(false);

        var statusChange = new Change<MediaStatusInfo> { Remove = true };
        await Commander.Call(new MediaStatusBackend_Change(mediaId, statusChange), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdateStatus(Medias_UpdateStatus command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, mediaId, status, preparingStage, stageProgress) = command;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var statusInfo = new MediaStatusInfo(mediaId, status, preparingStage, stageProgress);
        var change = new Change<MediaStatusInfo> { Update = statusInfo };
        await Commander.Call(new MediaStatusBackend_Change(mediaId, change), cancellationToken).ConfigureAwait(false);
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

        // Update status to Ready
        var statusInfo = new MediaStatusInfo(mediaId, MediaStatus.Ready, MediaPreparingStage.None, 100);
        var statusChange = new Change<MediaStatusInfo> { Update = statusInfo };
        await Commander.Call(new MediaStatusBackend_Change(mediaId, statusChange), cancellationToken).ConfigureAwait(false);

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
