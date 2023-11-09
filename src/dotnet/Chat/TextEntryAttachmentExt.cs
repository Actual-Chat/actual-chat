namespace ActualChat.Chat;

public static class TextEntryAttachmentExt
{
    public static bool IsSupportedImage(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSupportedImage(attachment.Media.ContentType);
    public static bool IsGif(this TextEntryAttachment attachment)
        => MediaTypeExt.IsGif(attachment.Media.ContentType);
    public static bool IsSvg(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSvg(attachment.Media.ContentType)
            || (attachment.IsSupportedImage() && OrdinalIgnoreCaseEquals(Path.GetExtension(attachment.Media.FileName), ".svg"));
    public static bool IsSupportedVideo(this TextEntryAttachment attachment)
        => attachment.Media.ContentType.OrdinalIgnoreCaseStartsWith("video");
    public static bool IsVisualMedia(this TextEntryAttachment attachment)
        => MediaTypeExt.IsSupportedVisualMedia(attachment.Media.ContentType);
}
