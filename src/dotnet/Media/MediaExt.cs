namespace ActualChat.Media;

public static class MediaExt
{
    public static MediaContent ToMediaContent(this Media media)
        => new (media.Id, media.ContentId);
}
