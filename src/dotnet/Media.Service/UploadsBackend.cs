using ActualChat.Blobs.Internal;
using ActualChat.Chat;
using ActualChat.Media.Db;
using ActualChat.Uploads;
using ActualLab.Fusion.EntityFramework;
using Google.Cloud.Storage.V1;

namespace ActualChat.Media;

public class UploadsBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IUploadsBackend
{
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();
    private GoogleResumableUploads GoogleResumableUploads => field ??= new GoogleResumableUploads(StorageClient.Create(), Services.LogFor<GoogleResumableUploads>());
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();

    private bool IsGoogleStorage => Blobs is GoogleCloudBlobStorages;

    public virtual async Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken)
    {
        var json = await UploadsStorage.GetMetadataFile(uploadId, cancellationToken).ConfigureAwait(false);
        var upload = json.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<Upload>(json);
        return upload;
    }

    public virtual async Task<long> GetOffset(UploadId uploadId, CancellationToken cancellationToken)
    {
        if (IsGoogleStorage) {
            var upload = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
            var sessionUri = upload.SessionUri.Require();
            var offset = await GoogleResumableUploads.GetUploadStatusAsync(sessionUri, cancellationToken).ConfigureAwait(false);
            return offset ?? upload.Length!.Value;
        }
        else {
            var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
            return offset ?? throw UploadNotFound();
        }
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
        if (IsGoogleStorage) {
            var location = await InitiateUploadSession(upload, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Upload session for upload '{UploadId}' initiated: '{Location}'", uploadId, location.AbsoluteUri);
            upload = upload with { SessionUri = location.AbsoluteUri };
            var json = JsonSerializer.Serialize(upload);
            await UploadsStorage.CreateMetadataFile(uploadId, json, cancellationToken).ConfigureAwait(false);
        }
        else {
            var json = JsonSerializer.Serialize(upload);
            await UploadsStorage.CreateMetadataFile(uploadId, json, cancellationToken).ConfigureAwait(false);
            await UploadsStorage.CreateEmptyDataFile(uploadId, contentType, cancellationToken).ConfigureAwait(false);
        }
        await TriggerDistributedInvalidation(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken)
    {
        var uploadId = command.Id;
        if (Invalidation.IsActive) {
            _ = Get(uploadId, default);
            return;
        }
        if (IsGoogleStorage) {
            var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
            if (upload is not null)
                await GoogleResumableUploads.CancelUpload(upload.SessionUri.Require(), cancellationToken).ConfigureAwait(false);
        }
        await UploadsStorage.DeleteFiles(uploadId, cancellationToken).ConfigureAwait(false);
        await TriggerDistributedInvalidation(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> OnAppend(UploadsBackend_Append command, CancellationToken cancellationToken)
    {
        var (uploadId, uploadOffset, data) = command;
        if (IsGoogleStorage) {
            var upload1 = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
            var sessionUri = upload1.SessionUri.Require();
            _ = await GoogleResumableUploads.UploadChunk(sessionUri, data, uploadOffset, upload1.Length!.Value, cancellationToken).ConfigureAwait(false);
            return uploadOffset + data.Length;
        }

        // NOTE(DF): In production environment UploadsStorage is backed with gcp blob storage.
        // It means that the last chunk of data is always reliably appended.
        // We don't need to check manually where the last chunk starts and if the append operation was successfully completed.
        // If it's not the case, we'll bump into the offset conflict scenario and the client will restart upload from the actual offset.
        // Nothing to do from our side.
        // In the local environment UploadsStorage uses the local file system,
        // but for simplicity we skip the check whether the last chunk was reliably appended.
        var currentOffset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (currentOffset is null)
            throw UploadNotFound();

        if (uploadOffset != currentOffset)
            throw StandardError.Upload.OffsetConflict();

        var upload = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        var expectedNewOffset = currentOffset.Value + data.Length;
        if (expectedNewOffset > upload.Length)
            throw StandardError.Constraint( $"Stream contains more data than the file's upload length. Stream data: {upload.Length}, upload length: {expectedNewOffset}.");

        var stream = new MemoryStream(data, writable: false);
        await using (stream.ConfigureAwait(false))
            await UploadsStorage.AppendDataAsync(uploadId, stream, cancellationToken).ConfigureAwait(false);
        return expectedNewOffset;
    }

    public virtual async Task<MediaContent> OnConvertToMediaContent(UploadsBackend_ConvertToMediaContent command, CancellationToken cancellationToken)
    {
        var uploadId = command.UploadId;
        var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
            throw UploadNotFound();

        var chatId = GetChatIdFromUploadTag(upload.Tag);
        var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
        if (offset != upload.Length)
            throw StandardError.Constraint("Upload length mismatch. Upload has not been completed.");

        var uploadedStreamFile = new UploadedStreamFile(
            upload.FileName,
            upload.ContentType,
            upload.Length!.Value,
            () => UploadsStorage.GetDataFile(uploadId, cancellationToken));
        return await MediaProcessor.ProcessAttachment(chatId, uploadedStreamFile, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Uri> InitiateUploadSession(Upload upload, CancellationToken cancellationToken)
    {
        var objectName = UploadsStorage.GetDataFileId(upload.Id);
        var bucketName = ((GoogleCloudBlobStorages)Blobs).BucketName;
        var location = await GoogleResumableUploads.StorageClient.InitiateUploadSessionAsync(
                bucketName,
                objectName,
                upload.ContentType,
                upload.Length,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        return location;
    }

    private async Task TriggerDistributedInvalidation(CancellationToken cancellationToken)
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

    private static Exception UploadNotFound()
        => StandardError.Upload.NotFound();
}
