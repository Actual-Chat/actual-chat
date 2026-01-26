using ActualChat.Flows;
using ActualChat.Media.Flows;

namespace ActualChat.Media;

public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private ICommander Commander { get; } = services.Commander();
    private FlowHub FlowHub => field ??= services.FlowHub();

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
    public virtual async Task OnStartProcessUpload(Uploads_StartProcessUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, uploadId, mediaId) = command;

        // Verify upload ownership
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);

        // Verify media ownership
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();
        if (media.UserId != user.Id)
            throw StandardError.Unauthorized("You don't have permission to access this media.");

        // Schedule the UploadProcessingFlow
        await FlowHub
            .NewResumeEvent<UploadProcessingFlow>(UploadProcessingFlow.GetArguments(uploadId, mediaId))
            .Schedule(cancellationToken)
            .ConfigureAwait(false);
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
