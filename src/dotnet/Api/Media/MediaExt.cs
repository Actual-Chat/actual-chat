namespace ActualChat.Media;

public static class MediaExt
{
    public static Picture? ToPicture(this Media? media, string? externalPictureUrl = null, string? avatarKey = null)
        => media == null && externalPictureUrl.IsNullOrEmpty() && avatarKey.IsNullOrEmpty()
            ? null
            : new (media?.ToMediaContent(), externalPictureUrl, avatarKey);

    public static MediaContent ToMediaContent(this Media media)
        => new (media.Id, media.ContentId);
}
