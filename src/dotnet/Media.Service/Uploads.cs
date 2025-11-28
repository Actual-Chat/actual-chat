using ActualChat.Users;

namespace ActualChat.Media;

public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<long> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        if (upload.UserId != user.Id)
            throw StandardError.Unauthorized("You can access only your own uploads.");

        var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        return offset;
    }

    // [CommandHandler]
    public virtual async Task<UploadId> OnCreate(Uploads_Create command, CancellationToken cancellationToken)
    {
        var (session, scope, length, metadata) = command;
        if (length is null)
            throw StandardError.NotSupported("Defer upload length is not supported yet.");

        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Commander.Call(new UploadsBackend_Create(user.Id, scope, length, metadata), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemove(Uploads_Remove command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
            return;

        if (upload.UserId != user.Id)
            throw StandardError.Unauthorized("You can remove only your own uploads.");

        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<long> OnAppend(Uploads_Append command, CancellationToken cancellationToken)
    {
        var (session, uploadId, data, offset) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        if (upload.UserId != user.Id)
            throw StandardError.Unauthorized("You can append only your own uploads.");

        var currentOffset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (offset != currentOffset)
            throw StandardError.Constraint("Offset mismatch.");

        var stream = MemoryStreamManager.Default.GetStream(nameof(Uploads), data.Length);
        await using (stream.ConfigureAwait(false))
            await UploadsStorage.AppendDataAsync(uploadId, stream, cancellationToken).ConfigureAwait(false);
        return currentOffset + data.Length;
    }

    // [CommandHandler]
    public virtual Task<MediaContent> OnComplete(Uploads_Complete command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
