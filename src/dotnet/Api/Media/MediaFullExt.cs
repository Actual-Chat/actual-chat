namespace ActualChat.Media;

public static class MediaFullExt
{
    public static Media ToMedia(this MediaFull media)
        => new (media.Id) {
            ContentId = media.ContentId,
            Metadata = media.Metadata,
        };
}
