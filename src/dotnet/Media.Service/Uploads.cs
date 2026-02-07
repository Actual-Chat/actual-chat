namespace ActualChat.Media;

public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaStatusBackend MediaStatusBackend { get; } = services.GetRequiredService<IMediaStatusBackend>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<long> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Backend.GetOffset(uploadId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UploadId> OnCreate(Uploads_Create command, CancellationToken cancellationToken)
    {
        var (session, length, tag, metadata) = command;
        if (length is null)
            throw StandardError.NotSupported("Defer upload length is not supported yet.");
        if (length > Constants.Attachments.FileSizeLimit)
            throw StandardError.Constraint("File is too big.");

        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        EnsureCanCreate(user, length.Value, tag, metadata);
        var uploadId = UploadId.New();
        await Commander.Call(new UploadsBackend_Create(uploadId, user.Id, length, tag, metadata), cancellationToken).ConfigureAwait(false);
        return uploadId;
    }

    // [CommandHandler]
    public virtual async Task OnRemove(Uploads_Remove command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null || upload.UserId != user.Id)
            return;

        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<long> OnAppend(Uploads_Append command, CancellationToken cancellationToken)
    {
        var (session, uploadId, offset, data) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Commander.Call(new UploadsBackend_Append(uploadId, offset, data), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<MediaContent> OnConvertToMediaContent(Uploads_ConvertToMediaContent command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Commander.Call(new UploadsBackend_ConvertToMediaContent(uploadId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnLinkWithMedia(Uploads_LinkWithMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, uploadId, mediaIds) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);

        // TODO(DF): store link data on upload as well.
        foreach (var mediaId in mediaIds) {
            var media = await MediaBackend.GetFull(mediaId, cancellationToken).Require().ConfigureAwait(false);
            if (media.UserId != user.Id)
                throw StandardError.Unauthorized("You don't have permission to access this media.");
            media = media with {
                UploadId = uploadId
            };
            var changeMedia = new Change<MediaFull> { Update = media };
            await Commander.Call(new MediaBackend_Change(mediaId, changeMedia), cancellationToken).ConfigureAwait(false);

            var statusInfo = await MediaStatusBackend.Get(mediaId, cancellationToken).Require().ConfigureAwait(false);
            statusInfo = statusInfo with {
                Status = MediaStatus.Preparing,
                PreparingStage = MediaPreparingStage.Uploading,
                StageProgress = 0,
            };
            var changeStatus = new Change<MediaStatusInfo> { Update = statusInfo };
            await Commander.Call(new MediaStatusBackend_Change(mediaId, changeStatus), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureCanAccessUpload([NotNullWhen(true)] Upload? upload, Account user)
    {
        if (upload is null || upload.UserId != user.Id)
            throw StandardError.Upload.NotFound();
    }

    private void EnsureCanCreate(AccountFull user, long length, string tag, PropertyBag metadata)
    {
        // Add validation by tag and user permissions
    }
}
