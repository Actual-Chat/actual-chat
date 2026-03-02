namespace ActualChat.Media;

public static class MediaFullExt
{
    public static Media ToMedia(this MediaFull media)
        => new (media.Id) {
            ContentId = media.ContentId,
            Kind = media.Kind,
            Metadata = media.Metadata,
        };
}
