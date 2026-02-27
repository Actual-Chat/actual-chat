namespace ActualChat.Media;

public static class MediaFullExt
{
    public static Media ToMedia(this MediaFull media)
        => new (media.Id) {
            BlobId = media.BlobId,
            Metadata = media.Metadata,
        };
}
