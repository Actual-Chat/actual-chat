using ActualChat.Chat;
using ActualChat.Media.Db;
using ActualChat.Uploads;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class UploadsBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IUploadsBackend
{
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();

    public virtual async Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken)
    {
        var json = await UploadsStorage.GetMetadataFile(uploadId, cancellationToken).ConfigureAwait(false);
        return json.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<Upload>(json);
    }

    public virtual async Task<long> GetOffset(UploadId uploadId, CancellationToken cancellationToken)
    {
        var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        return offset ?? throw StandardError.NotFound<Upload>();
    }

    public virtual async Task OnCreate(UploadsBackend_Create command, CancellationToken cancellationToken)
    {
        var uploadId = command.UploadId;
        if (Invalidation.IsActive) {
            _ = Get(uploadId, default);
            return;
        }
        var upload = new Upload(uploadId, command.UserId, command.Length, command.Tag, command.Metadata);
        var contentType = upload.ContentType.NullIfEmpty() ?? "application/octet-stream";
        var json = JsonSerializer.Serialize(upload);
        await UploadsStorage.CreateMetadataFile(uploadId, json, cancellationToken).ConfigureAwait(false);
        await UploadsStorage.CreateEmptyDataFile(uploadId,  contentType, cancellationToken).ConfigureAwait(false);
        await EnsureDbOperationCreated(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken)
    {
        var uploadId = command.Id;
        if (Invalidation.IsActive) {
            _ = Get(uploadId, default);
            return;
        }
        await UploadsStorage.DeleteFiles(uploadId, cancellationToken).ConfigureAwait(false);
        await EnsureDbOperationCreated(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> OnAppend(UploadsBackend_Append command, CancellationToken cancellationToken)
    {
        var (uploadId, offset, data) = command;
        var currentOffset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (currentOffset is null)
            throw StandardError.NotFound<Upload>();

        if (offset != currentOffset)
            throw StandardError.OffsetConflict();

        var upload = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        if (offset + data.Length > upload.Length)
            throw StandardError.Constraint("Upload length mismatch.");

        var stream = MemoryStreamManager.Default.GetStream(nameof(Uploads), data.Length);
        await using (stream.ConfigureAwait(false)) {
            stream.Write(data);
            stream.Position = 0;
            await UploadsStorage.AppendDataAsync(uploadId, stream, cancellationToken).ConfigureAwait(false);
        }
        return currentOffset.Value + data.Length;
    }

    public virtual async Task<MediaContent> OnConvertToMediaContent(UploadsBackend_ConvertToMediaContent command, CancellationToken cancellationToken)
    {
        var uploadId = command.UploadId;
        var upload = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
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

    private async Task EnsureDbOperationCreated(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
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
