using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Chat;

public static class MediaSaverExt
{
    public static Task<Media.Media> Save(this IMediaSaver mediaSaver, ChatId chatId, UploadedFile file, Size? size, CancellationToken cancellationToken)
        => mediaSaver.Save(MediaId.New(chatId.Value), file, size, cancellationToken);
}
