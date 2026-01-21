using ActualChat.Uploads;

namespace ActualChat.Media;

public class MediaProcessor(IServiceProvider services) : IMediaProcessor
{
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();

    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();

    public async Task<MediaContent> ProcessAttachment(
        ChatId chatId,
        UploadedFile uploadedFile,
        CancellationToken cancellationToken)
    {
        using var processedFile = await UploadProcessors.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
        var media = await MediaSaver.Save(chatId, processedFile.File, processedFile.Size, cancellationToken)
            .ConfigureAwait(false);
        if (processedFile.Thumbnail == null)
            return new MediaContent(media.Id, media.ContentId);

        var thumbnailMedia = await MediaSaver
            .Save(chatId, processedFile.Thumbnail, processedFile.Size, cancellationToken)
            .ConfigureAwait(false);
        return new MediaContent(media.Id, media.ContentId, thumbnailMedia.Id, thumbnailMedia.ContentId);
    }
}
