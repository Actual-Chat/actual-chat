using ActualChat.Users;

namespace ActualChat.Media;

public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<Result<long>> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (!CanAccessUpload(upload, user, out var error))
            return Result.NewError<long>(error);

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
    public virtual async Task<Result<long>> OnAppend(Uploads_Append command, CancellationToken cancellationToken)
    {
        var (session, uploadId, offset, data) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        if (!CanAccessUpload(upload, user, out var error))
            return Result.NewError<long>(error);

        return await Commander.Call(new UploadsBackend_Append(uploadId, offset, data), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Result<MediaContent>> OnConvertToMediaContent(Uploads_ConvertToMediaContent command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (!CanAccessUpload(upload, user, out var error))
            return Result.NewError<MediaContent>(error);

        return await Commander.Call(new UploadsBackend_ConvertToMediaContent(uploadId), cancellationToken).ConfigureAwait(false);
    }

    private static bool CanAccessUpload([NotNullWhen(true)] Upload? upload, Account user, [NotNullWhen(false)] out Exception? error)
    {
        if (upload is null || upload.UserId != user.Id) {
            error = StandardError.NotFound<Upload>();
            return false;
        }
        error = null;
        return true;
    }

    private void EnsureCanCreate(AccountFull user, long length, string tag, PropertyBag metadata)
    {
        // Add validation by tag and user permissions
    }
}
