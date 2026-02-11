using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Media;

public sealed class MediaSaver(IServiceProvider services) : IMediaSaver
{
    private IContentSaver ContentSaver { get; } = services.GetRequiredService<IContentSaver>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private ICommander Commander { get; } = services.Commander();

    public async Task<MediaContent> Save(MediaId mediaId, ProcessedFile processedFile, bool isUpdate, CancellationToken cancellationToken)
    {
        var mediaContent = GetContentId(mediaId, processedFile);
        await SaveFileContent(processedFile.File, mediaContent.ContentId, cancellationToken).ConfigureAwait(false);
        await SaveMediaMetadata(mediaId, mediaContent.ContentId, processedFile.File, processedFile.Size, isUpdate, cancellationToken).ConfigureAwait(false);
        await SetMediaStatusToReady(mediaId, cancellationToken).ConfigureAwait(false);
        if (processedFile.Thumbnail != null) {
            await SaveFileContent(processedFile.Thumbnail, mediaContent.ThumbnailContentId!, cancellationToken).ConfigureAwait(false);
            await SaveMediaMetadata(mediaContent.ThumbnailMediaId!, mediaContent.ThumbnailContentId!, processedFile.Thumbnail, processedFile.Size, false, cancellationToken).ConfigureAwait(false);
            await SetMediaStatusToReady(mediaContent.ThumbnailMediaId!, cancellationToken).ConfigureAwait(false);
        }
        return mediaContent;
    }

    public Task<MediaContent> Save(
        MediaId mediaId,
        UploadedFile file,
        Size? size,
        CancellationToken cancellationToken)
        => Save(mediaId, new ProcessedFile(file, size, null), false, cancellationToken);

    private string GetContentId(MediaId mediaId, UploadedFile file)
        => mediaId.GetContentId(Path.GetExtension(file.FileName));

    private MediaContent GetContentId(MediaId mediaId, ProcessedFile file)
    {
        var contentId = GetContentId(mediaId, file.File);
        if (file.Thumbnail == null)
            return new MediaContent(mediaId, contentId);

        var thumbnailMediaId = MediaId.New(mediaId.Scope);
        var thumbnailContentId = GetContentId(thumbnailMediaId, file.Thumbnail);
        return new MediaContent(mediaId, contentId, thumbnailMediaId, thumbnailContentId);
    }

    private async Task SaveFileContent(UploadedFile file, string contentId, CancellationToken cancellationToken)
    {
        var stream = await file.Open().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false)) {
            var content = new Content(contentId, file.ContentType, stream);
            await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveMediaMetadata(
        MediaId mediaId,
        string contentId,
        UploadedFile file,
        Size? size,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        MediaFull? media;
        if (isUpdate)
            media = await MediaBackend.GetFull(mediaId, cancellationToken).Require().ConfigureAwait(false);
        else
            media = new MediaFull(mediaId);
        media = media with {
            ContentId = contentId,
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
        };
        var change = isUpdate
            ? new Change<MediaFull> { Update = media }
            : new Change<MediaFull> { Create = media };
        var changeCommand = new MediaBackend_Change(mediaId, change);
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetMediaStatusToReady(MediaId mediaId, CancellationToken cancellationToken)
    {
        var changeStatus = Change.Update(new MediaStatusInfo(mediaId, MediaStage.Ready, 100, ""));
        await Commander.Run(new MediaStatusBackend_Change(mediaId, changeStatus), cancellationToken).ConfigureAwait(false);
    }
}
