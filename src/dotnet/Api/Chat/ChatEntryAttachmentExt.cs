namespace ActualChat.Chat;

public static class ChatEntryAttachmentExt
{
    public static bool IsSupportedImage(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsSupportedImage(attachment.Media.ContentType);
    public static bool IsGif(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsGif(attachment.Media.ContentType);
    public static bool IsSvg(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsSvg(attachment.Media.ContentType)
            || (attachment.IsSupportedImage() && string.Equals(Path.GetExtension(attachment.Media.FileName), ".svg", StringComparison.OrdinalIgnoreCase));
    public static bool IsSupportedVideo(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsSupportedVideo(attachment.Media.ContentType);
    public static bool IsVisualMedia(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsSupportedVisualMedia(attachment.Media.ContentType);
    public static bool IsAudio(this ChatEntryAttachment attachment)
        => MediaTypeExt.IsAudio(attachment.Media.ContentType);
}
