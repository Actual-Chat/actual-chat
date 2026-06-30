using System.Text;
using ActualChat.Hashing;
using ActualChat.Uploads;
namespace ActualChat.Media;

/// <summary>
/// Saves media files to blob storage and creates database records.
/// </summary>
public sealed class MediaSaver(IServiceProvider services) : IMediaSaver
{
    private IBlobStorage BlobStorage { get; } = services.BlobStorages()[BlobScope.ContentRecord];
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private ICommander Commander { get; } = services.Commander();

    // GetBlobId

    public static string GetBlobId(MediaId mediaId, string fileExt)
    {
        var mediaIdHash = mediaId.Value.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        return $"media/{mediaIdHash}/{mediaId.LocalId}{fileExt}";
    }

    public static string GetBlobId(MediaId mediaId, UploadedFile file)
        => GetBlobId(mediaId, Path.GetExtension(file.FileName));

    public static MediaRef GetMediaRef(MediaId mediaId, ProcessedFile file)
    {
        var blobId = GetBlobId(mediaId, file.File);
        if (file.Thumbnail == null)
            return new MediaRef(mediaId, blobId);

        var thumbnailMediaId = MediaId.New(mediaId.Scope);
        var thumbnailBlobId = GetBlobId(thumbnailMediaId, file.Thumbnail);
        return new MediaRef(mediaId, blobId, thumbnailMediaId, thumbnailBlobId);
    }

    // Save

    public async Task<MediaRef> Save(
        MediaId mediaId, ProcessedFile processedFile, bool isUpdate, MediaKind kind,
        CancellationToken cancellationToken)
    {
        var mediaRef = GetMediaRef(mediaId, processedFile);
        if (processedFile.Thumbnail != null) {
            await SaveFileContent(processedFile.Thumbnail, mediaRef.ThumbnailBlobId!, cancellationToken).ConfigureAwait(false);
            await SaveMediaMetadata(
                mediaRef.ThumbnailMediaId!,
                mediaRef.ThumbnailBlobId!,
                processedFile.Thumbnail,
                processedFile.Size,
                null,
                null,
                false,
                kind,
                cancellationToken)
                .ConfigureAwait(false);
            await SetMediaProgressToReady(mediaRef.ThumbnailMediaId!, cancellationToken).ConfigureAwait(false);
        }
        await SaveFileContent(processedFile.File, mediaRef.BlobId, cancellationToken).ConfigureAwait(false);
        await SaveMediaMetadata(
            mediaId,
            mediaRef.BlobId,
            processedFile.File,
            processedFile.Size,
            processedFile.Duration,
            mediaRef.ThumbnailMediaId,
            isUpdate,
            kind,
            cancellationToken).ConfigureAwait(false);
        await SetMediaProgressToReady(mediaId, cancellationToken).ConfigureAwait(false);
        return mediaRef;
    }

    public Task<MediaRef> Save(
        MediaId mediaId, UploadedFile file, Size2D? size, MediaKind kind,
        CancellationToken cancellationToken)
        => Save(mediaId, new ProcessedFile(file, size, null), false, kind, cancellationToken);

    // Private methods

    private async Task SaveFileContent(UploadedFile file, string blobId, CancellationToken cancellationToken)
    {
        if (file is UploadedBlobFile blobFile) {
            await BlobStorage.Copy(blobFile.BlobPath, blobId, cancellationToken).ConfigureAwait(false);
            return;
        }
        var stream = await file.Open().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            await BlobStorage.Write(blobId, stream, file.ContentType, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveMediaMetadata(
        MediaId mediaId,
        string blobId,
        UploadedFile file,
        Size2D? size,
        TimeSpan? duration,
        MediaId? thumbnailMediaId,
        bool isUpdate,
        MediaKind kind,
        CancellationToken cancellationToken)
    {
        MediaFull? media;
        if (isUpdate)
            media = await MediaBackend.GetFull(mediaId, cancellationToken).Require().ConfigureAwait(false);
        else
            media = new MediaFull(mediaId) { Kind = kind };
        media = media with {
            BlobId = blobId,
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
            ThumbnailId = thumbnailMediaId,
        };
        if (duration is { } d)
            media = media with { DurationMs = (long)d.TotalMilliseconds };
        var change = isUpdate
            ? new Change<MediaFull> { Update = media }
            : new Change<MediaFull> { Create = media };
        var changeCommand = new MediaBackend_Change(mediaId, null, change);
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetMediaProgressToReady(MediaId mediaId, CancellationToken cancellationToken)
    {
        var changeProgress = Change.Update(new MediaProgress(mediaId, 0, MediaProcessingStage.Ready, 0));
        await Commander.Run(new MediaProgressBackend_Change(mediaId, null, changeProgress), cancellationToken).ConfigureAwait(false);
    }
}
