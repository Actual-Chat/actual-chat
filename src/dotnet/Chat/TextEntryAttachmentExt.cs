namespace ActualChat.Chat;

public static class TextEntryAttachmentExt
{
    public static bool IsImage(this TextEntryAttachment attachment)
        => attachment.ContentType.OrdinalIgnoreCaseStartsWith("image");
}
