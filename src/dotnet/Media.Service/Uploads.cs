using ActualChat.Chat;
using ActualChat.Uploads;
using ActualChat.Users;

namespace ActualChat.Media;

public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<long> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        return offset;
    }

    // [CommandHandler]
    public virtual async Task<UploadId> OnCreate(Uploads_Create command, CancellationToken cancellationToken)
    {
        var (session, length, tag, metadata) = command;
        if (length is null)
            throw StandardError.NotSupported("Defer upload length is not supported yet.");
        if (length > Constants.Attachments.FileSizeLimit)
            throw StandardError.Constraint("File is too big.");
        // TODO: add validation by tag and user permissions

        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Commander.Call(new UploadsBackend_Create(user.Id, length, tag, metadata), cancellationToken).ConfigureAwait(false);
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
        var (session, uploadId, data, offset) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);

        var currentOffset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (offset != currentOffset)
            throw StandardError.Constraint("Offset mismatch.");
        if (offset + data.Length > upload.Length)
            throw StandardError.Constraint("Upload length mismatch.");

        var stream = MemoryStreamManager.Default.GetStream(nameof(Uploads), data.Length);
        await using (stream.ConfigureAwait(false)) {
            stream.Write(data);
            stream.Position = 0;
            await UploadsStorage.AppendDataAsync(uploadId, stream, cancellationToken).ConfigureAwait(false);
        }
        return currentOffset + data.Length;
    }

    // [CommandHandler]
    public virtual async Task<MediaContent> OnConvertToMediaContent(Uploads_ConvertToMediaContent command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        var chatId = GetChatIdFromUploadTag(upload.Tag);
        var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (offset != upload.Length)
            throw StandardError.Constraint("Upload length mismatch.");

        var uploadedStreamFile = new UploadedStreamFile(
            upload.FileName,
            upload.ContentType,
            upload.Length!.Value,
            () => UploadsStorage.GetDataFile(uploadId, cancellationToken));
        return await MediaProcessor.ProcessAttachment(chatId, uploadedStreamFile, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureCanAccessUpload([NotNull] Upload? upload, Account user)
    {
        if (upload is null|| upload.UserId != user.Id)
            throw StandardError.Constraint("Upload not found.");
    }

    private ChatId GetChatIdFromUploadTag(string uploadTag)
    {
        var parts = uploadTag.Split('/');
        if (parts.Length == 3
            && OrdinalEquals(parts[0], nameof(TextEntryAttachment))
            && OrdinalEquals(parts[1], "v1")
            && ChatId.TryParse(parts[2], out var chatId))
            return chatId;

        throw StandardError.Constraint("Invalid upload tag.");
    }
}
