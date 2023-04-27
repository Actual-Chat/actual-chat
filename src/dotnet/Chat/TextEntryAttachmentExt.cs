namespace ActualChat.Chat;

public static class TextEntryAttachmentExt
{
    public static bool IsImage(this TextEntryAttachment attachment)
        => attachment.Media.ContentType.OrdinalIgnoreCaseStartsWith("image");
    public static bool IsVideo(this TextEntryAttachment attachment)
        => attachment.Media.ContentType.OrdinalIgnoreCaseStartsWith("video");
    public static bool IsVisualMedia(this TextEntryAttachment attachment)
        => attachment.IsImage() || attachment.IsVideo();
}
