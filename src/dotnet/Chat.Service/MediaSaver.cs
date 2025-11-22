using ActualChat.Media;
using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Chat;

// TODO(DF): to think where to put this.
public sealed class MediaSaver(ICommander commander, IContentSaver contentSaver) : IMediaSaver
{
    public async Task<Media.Media> Save(
        MediaId mediaId,
        UploadedFile file,
        Size? size,
        CancellationToken cancellationToken)
    {
        var media = new Media.Media(mediaId) {
            ContentId = mediaId.GetContentId(Path.GetExtension(file.FileName)),
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
        return await commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false)!;
    }
}
