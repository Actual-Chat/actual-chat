using ActualChat.Media;
using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Chat;

public sealed class MediaStorage(ICommander commander, IContentSaver contentSaver)
{
    public Task<Media.Media?> Save(ChatId chatId, UploadedFile file, Size? size, CancellationToken cancellationToken)
        => Save(file, size, new MediaId(chatId, Generate.Option), cancellationToken);

    public async Task<Media.Media?> Save(UploadedFile file, Size? size, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = new Media.Media(mediaId) {
            ContentId = mediaId.ContentId(Path.GetExtension(file.FileName)),
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
        };
        var stream = await file.Open().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false)) {
            var content = new Content(media.ContentId, file.ContentType, stream);
            await contentSaver.Save(content, cancellationToken).ConfigureAwait(false);
        }

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media.Media> {
                Create = media,
            });
        return await commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
