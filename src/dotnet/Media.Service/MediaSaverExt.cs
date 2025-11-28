using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Media;

public static class MediaSaverExt
{
    public static Task<Media> Save(this IMediaSaver mediaSaver, ChatId chatId, UploadedFile file, Size? size, CancellationToken cancellationToken)
        => mediaSaver.Save(MediaId.New(chatId.Value), file, size, cancellationToken);
}
