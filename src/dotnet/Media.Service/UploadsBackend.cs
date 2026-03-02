using ActualChat.Blobs.Internal;
using ActualChat.Media.Db;
using ActualChat.Uploads;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Google.Cloud.Storage.V1;

namespace ActualChat.Media;

/// <summary>
/// Backend service implementation for managing file uploads including resumable uploads.
/// </summary>
public class UploadsBackend(IServiceProvider services) : DbServiceBase<MediaDbContext>(services), IUploadsBackend
{
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();
    private GoogleResumableUploads GoogleResumableUploads => field ??= new GoogleResumableUploads(StorageClient.Create(), Services.LogFor<GoogleResumableUploads>());
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();
    private IMediaProgressBackend MediaProgressBackend => field ??= Services.GetRequiredService<IMediaProgressBackend>();

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
            var offset = await GoogleResumableUploads.GetUploadStatusAsync(sessionUri, cancellationToken)
                .ConfigureAwait(false);
            offset ??= upload.Length!.Value;
            return offset.Value;
        }
        else {
            var offset = await UploadsStorage.GetUploadOffset(uploadId, cancellationToken).ConfigureAwait(false);
            if (offset is null)
                throw UploadNotFound();

            return offset.Value;
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
        var contentType = upload.ContentType.NullIfEmpty() ?? MediaMimeTypes.GetMimeType(upload.FileName);
        upload = upload with { ContentType = contentType };
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
            var newOffset = uploadOffset + data.Length;
            return newOffset;
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
        if (Invalidation.IsActive)
            return default!;

        var uploadId = command.UploadId;
        var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
            throw UploadNotFound();

        await EnsureUploadHasBeenCompleted(upload, cancellationToken).ConfigureAwait(false);

        var uploadedFile = GetUploadedStreamFileFrom(upload, cancellationToken);
        MediaId mediaId;
        if (upload.Tag.IsNullOrEmpty())
            mediaId = MediaId.New(MediaId.NewScope());
        else {
            var chatId = upload.ExtractChatIdFromTag();
            mediaId = MediaId.New(chatId.Value);
        }
        using var processedFile = await MediaProcessor.ProcessUpload(uploadedFile, cancellationToken).ConfigureAwait(false);
        return await MediaSaver.Save(mediaId, processedFile, isUpdate:false, MediaKind.ChatEntryAttachment, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<MediaContent> OnProcessAndSaveContent(UploadsBackend_ProcessAndSaveContent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (uploadId, mediaId) = command;
        ThrottledProgress<double>? progress = null;
        try {
            var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
            if (upload == null)
                throw UploadNotFound();

            await EnsureUploadHasBeenCompleted(upload, cancellationToken).ConfigureAwait(false);

            var uploadedFile = GetUploadedStreamFileFrom(upload, cancellationToken);

            progress = CreateMediaConvertingProgressTracker(mediaId);
            using var processedFile = await MediaProcessor.ProcessUpload(uploadedFile, progress, cancellationToken)
                .ConfigureAwait(false);
            var mediaContent = await MediaSaver.Save(mediaId, processedFile, isUpdate: true, default, cancellationToken)
                .ConfigureAwait(false);
            return mediaContent;
        }
        catch (Exception e) {
            Log.LogError(e,
                "Failed to process and save content for upload '{UploadId}' and media '{MediaId}'",
                uploadId,
                mediaId);
            await ReportMediaServerProcessingError(
                MediaProgressBackend,
                Commander,
                mediaId,
                "",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally {
            progress?.Dispose();
        }
    }

    internal static async ValueTask ReportMediaServerProcessingError(
        IMediaProgressBackend mediaProgressBackend,
        ICommander commander,
        MediaId mediaId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var mediaProgress = await mediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (mediaProgress is null
            || mediaProgress.Stage == MediaStage.Ready
            || mediaProgress.Stage == MediaStage.ServerProcessing && !mediaProgress.ErrorMessage.IsNullOrEmpty())
            return;

        var progress = mediaProgress.Stage is MediaStage.ServerProcessing ? mediaProgress.StageProgress : 0;
        var failedProgress = new MediaProgress(mediaId,
            0,
            MediaStage.ServerProcessing,
            progress,
            errorMessage.NullIfEmpty() ?? "Failed to process upload");
        var failedChange = new Change<MediaProgress> { Update = failedProgress };
        var change = new MediaProgressBackend_Change(mediaId, null, failedChange);
        await commander.Call(change, true, cancellationToken).ConfigureAwait(false);
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

    private static Exception UploadNotFound()
        => StandardError.Upload.NotFound();

    private async Task EnsureUploadHasBeenCompleted(Upload upload, CancellationToken cancellationToken)
    {
        if (upload.Length is null)
            throw StandardError.Constraint("Upload length is not set.");

        var offset = await UploadsStorage.GetUploadOffset(upload.Id, cancellationToken).ConfigureAwait(false);
        if (offset != upload.Length)
            throw StandardError.Constraint("Upload length mismatch. Upload has not been completed.");
    }

    private UploadedStreamFile GetUploadedStreamFileFrom(Upload upload, CancellationToken cancellationToken)
    {
        var uploadedFile = new UploadedStreamFile(
            upload.FileName,
            upload.ContentType,
            upload.Length!.Value,
            () => UploadsStorage.GetDataFile(upload.Id, cancellationToken));
        return uploadedFile;
    }

    private ThrottledProgress<double> CreateMediaConvertingProgressTracker(MediaId mediaId)
    {
        var progress = new ThrottledProgress<double>(p => {
            // Fire and forget - we don't want to block processing for status updates
            _ = UpdateMediaProgress(MediaStage.ServerProcessing, p, CancellationToken.None);
        }, TimeSpan.FromSeconds(1));
        return progress;

        async Task UpdateMediaProgress(MediaStage stage, double p, CancellationToken cancellationToken)
        {
            try {
                var mediaProgress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
                if (mediaProgress is null || mediaProgress.Stage == MediaStage.Ready || !mediaProgress.ErrorMessage.IsNullOrEmpty())
                    return;

                mediaProgress = new MediaProgress(mediaId, mediaProgress.Version, stage, p, "");
                var change = new Change<MediaProgress> { Update = mediaProgress };
                await Commander.Call(new MediaProgressBackend_Change(mediaId, mediaProgress.Version, change), true, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
            catch(VersionMismatchException) {}
            catch (Exception e) {
                Log.LogWarning(e, "Failed to update media progress for '{MediaId}'", mediaId);
            }
        }
    }
}
