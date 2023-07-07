namespace ActualChat.Media;

public static class MediaExt
{
    public static Picture? ToPicture(this Media? media, string? externalPictureUrl = null)
        => media == null && externalPictureUrl.IsNullOrEmpty()
            ? null
            : new (media?.ToMediaContent(), externalPictureUrl);

    public static MediaContent ToMediaContent(this Media media)
        => new (media.Id, media.ContentId);
}
