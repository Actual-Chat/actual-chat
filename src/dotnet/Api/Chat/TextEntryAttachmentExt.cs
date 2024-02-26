using ActualChat.Media;

namespace ActualChat.Chat;

public static class TextEntryAttachmentExt
{
    public static bool IsSupportedImage(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSupportedImage(attachment.Media?.ContentType);
    public static bool IsGif(this TextEntryAttachment attachment)
        => MediaTypeExt.IsGif(attachment.Media?.ContentType);
    public static bool IsSvg(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSvg(attachment.Media?.ContentType)
            || (attachment.IsSupportedImage() && OrdinalIgnoreCaseEquals(Path.GetExtension(attachment.Media?.FileName), ".svg"));
    public static bool IsSupportedVideo(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSupportedVideo(attachment.Media?.ContentType);
    public static bool IsVisualMedia(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSupportedVisualMedia(attachment.Media?.ContentType);

    public static TextEntryAttachment WithMedia(
        this TextEntryAttachment attachment,
        Func<MediaId, Media.Media?> getMedia)
    {
        if (attachment.MediaId.IsNone)
            return attachment;

        var media = getMedia(attachment.MediaId) ?? attachment.Media;
        var thumbnailMedia = attachment.ThumbnailMedia;
        if (!attachment.ThumbnailMediaId.IsNone)
            thumbnailMedia = getMedia(attachment.ThumbnailMediaId) ?? thumbnailMedia;
        return attachment with {
            Media = media,
            ThumbnailMedia = thumbnailMedia,
        };
    }
}
