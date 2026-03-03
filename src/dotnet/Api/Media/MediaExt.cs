namespace ActualChat.Media;

/// <summary>
/// Extension methods for <see cref="Media"/>.
/// </summary>
public static class MediaExt
{
    public static Picture? ToPicture(this Media? media, string? externalPictureUrl = null, string? avatarKey = null)
        => media == null && externalPictureUrl.IsNullOrEmpty() && avatarKey.IsNullOrEmpty()
            ? null
            : new (media?.ToMediaRef(), externalPictureUrl, avatarKey);

    public static MediaRef ToMediaRef(this Media media)
        => new (media.Id, media.BlobId);
}
