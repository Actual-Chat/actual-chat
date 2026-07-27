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
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IMediaProgressBackend MediaProgressBackend => field ??= Services.GetRequiredService<IMediaProgressBackend>();
    private IMeshLocks ConvertToMediaRefLocks => field ??= Services.MeshLocks()
        .WithKeyPrefix($"{nameof(UploadsBackend)}.{nameof(OnConvertToMediaRef)}");

    private bool IsGoogleStorage => Blobs is GoogleCloudBlobStorages;

    public virtual async Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken)
    {
        var json = await UploadsStorage.GetMetadataFile(uploadId, cancellationToken).ConfigureAwait(false);
        var upload = json.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<Upload>(json);
        return upload;
    }

    public async Task<long> GetOffset(UploadId uploadId, CancellationToken cancellationToken)
    {
        if (IsGoogleStorage) {
            var upload = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
            var sessionUri = upload.SessionUri.Require();
            var offset = await GoogleResumableUploads
                .GetUploadStatusAsync(sessionUri, cancellationToken)
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
        if (Invalidation.IsActive)
            return default!; // Invalidation is not expected to happen during the append operation, but just in case.

        var (uploadId, uploadOffset, data) = command;
        if (IsGoogleStorage) {
            var upload1 = await Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
            var sessionUri = upload1.SessionUri.Require();
            _ = await GoogleResumableUploads
                .UploadChunk(sessionUri, data, uploadOffset, upload1.Length!.Value, cancellationToken)
                .ConfigureAwait(false);
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

    public virtual async Task<MediaRef> OnConvertToMediaRef(UploadsBackend_ConvertToMediaRef command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var uploadId = command.UploadId;
        var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null)
            throw UploadNotFound();

        await EnsureUploadHasBeenCompleted(upload, cancellationToken).ConfigureAwait(false);

        var mediaId = GetConvertedMediaId(upload);
        return await ConvertToMediaRefLocks
            .LockAndRun(
                uploadId.Value,
                async ct => await Convert(mediaId, upload, ct).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

        async Task<MediaRef> Convert(MediaId mediaId1, Upload upload1, CancellationToken cancellationToken1)
        {
            if (await GetMediaRef(mediaId1, cancellationToken1).ConfigureAwait(false) is { } existing)
                return existing;

            var uploadedFile = GetUploadedStreamFileFrom(upload1, cancellationToken1);
            using var processedFile = await MediaProcessor
                .ProcessUpload(
                    uploadedFile,
                    MediaKind.ChatEntryAttachment,
                    null,
                    cancellationToken1)
                .ConfigureAwait(false);
            return await MediaSaver
                .Save(
                    mediaId1,
                    processedFile,
                    isUpdate: false,
                    MediaKind.ChatEntryAttachment,
                    cancellationToken1)
                .ConfigureAwait(false);
        }
    }

    public virtual async Task<MediaRef> OnProcessAndSaveContent(UploadsBackend_ProcessAndSaveContent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (uploadId, mediaId) = command;
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        ThrottledProgress<double>? progress = null;
        try {
            var upload = await Get(uploadId, cancellationToken).ConfigureAwait(false);
            if (upload == null)
                throw UploadNotFound();

            await EnsureUploadHasBeenCompleted(upload, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("OnProcessAndSaveContent: upload validated in {Elapsed}ms (upload '{UploadId}', media '{MediaId}')",
                stepSw.ElapsedMilliseconds, uploadId, mediaId);

            var uploadedFile = GetUploadedStreamFileFrom(upload, cancellationToken);

            var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
            var mediaKind = media?.Kind ?? MediaKind.Unknown;

            stepSw.Restart();
            progress = CreateMediaConvertingProgressTracker(mediaId);
            using var processedFile = await MediaProcessor.ProcessUpload(uploadedFile, mediaKind, progress, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("OnProcessAndSaveContent: ProcessUpload completed in {Elapsed}ms (upload '{UploadId}', media '{MediaId}')",
                stepSw.ElapsedMilliseconds, uploadId, mediaId);

            stepSw.Restart();
            var mediaRef = await MediaSaver
                .Save(mediaId, processedFile, isUpdate: true, mediaKind, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("OnProcessAndSaveContent: MediaSaver.Save completed in {Elapsed}ms (upload '{UploadId}', media '{MediaId}')",
                stepSw.ElapsedMilliseconds, uploadId, mediaId);

            Log.LogDebug("OnProcessAndSaveContent: total {Elapsed}ms (upload '{UploadId}', media '{MediaId}')",
                totalSw.ElapsedMilliseconds, uploadId, mediaId);
            return mediaRef;
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
        string error,
        CancellationToken cancellationToken)
    {
        var mediaProgress = await mediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (mediaProgress is null
            || mediaProgress.Stage == MediaProcessingStage.Ready
            || mediaProgress.Stage == MediaProcessingStage.ServerProcessing && !mediaProgress.Error.IsNullOrEmpty())
            return;

        var progress = mediaProgress.Stage is MediaProcessingStage.ServerProcessing ? mediaProgress.StageProgress : 0;
        var failedProgress = new MediaProgress(mediaId,
            0,
            MediaProcessingStage.ServerProcessing,
            progress,
            error.NullIfEmpty() ?? "Failed to process upload");
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

    private static MediaId GetConvertedMediaId(Upload upload)
    {
        var scope = upload.Tag.IsNullOrEmpty()
            ? upload.Id.Value
            : upload.ExtractChatIdFromTag().Value;
        return MediaId.New(scope, upload.Id.Value);
    }

    private async Task<MediaRef?> GetMediaRef(MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is null || media.BlobId.IsNullOrEmpty())
            return null;

        var thumbnailId = media.ThumbnailId;
        var thumbnail = thumbnailId is null
            ? null
            : await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false);
        return new MediaRef(mediaId, media.BlobId, thumbnailId, thumbnail?.BlobId);
    }

    private async Task EnsureUploadHasBeenCompleted(Upload upload, CancellationToken cancellationToken)
    {
        if (upload.Length is null)
            throw StandardError.Constraint("Upload length is not set.");

        var offset = await UploadsStorage.GetUploadOffset(upload.Id, cancellationToken).ConfigureAwait(false);
        if (offset != upload.Length)
            throw StandardError.Constraint("Upload length mismatch. Upload has not been completed.");
    }

    private UploadedFile GetUploadedStreamFileFrom(Upload upload, CancellationToken cancellationToken)
    {
        if (IsGoogleStorage) {
            var blobPath = UploadsStorage.GetDataFileId(upload.Id);
            return new UploadedBlobFile(
                upload.FileName,
                upload.ContentType,
                upload.Length!.Value,
                blobPath,
                () => UploadsStorage.GetDataFile(upload.Id, cancellationToken));
        }
        return new UploadedStreamFile(
            upload.FileName,
            upload.ContentType,
            upload.Length!.Value,
            () => UploadsStorage.GetDataFile(upload.Id, cancellationToken));
    }

    private ThrottledProgress<double> CreateMediaConvertingProgressTracker(MediaId mediaId)
    {
        var progress = new ThrottledProgress<double>(p => {
            // Fire and forget - we don't want to block processing for status updates
            _ = UpdateMediaProgress(MediaProcessingStage.ServerProcessing, p, CancellationToken.None);
        }, TimeSpan.FromSeconds(3));
        return progress;

        async Task UpdateMediaProgress(MediaProcessingStage processingStage, double p, CancellationToken cancellationToken)
        {
            try {
                var mediaProgress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
                if (mediaProgress is null || mediaProgress.Stage == MediaProcessingStage.Ready || !mediaProgress.Error.IsNullOrEmpty())
                    return;

                mediaProgress = new MediaProgress(mediaId, mediaProgress.Version, processingStage, p);
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
