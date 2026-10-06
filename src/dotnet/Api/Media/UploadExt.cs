namespace ActualChat.Media;

/// <summary>
/// Extension methods for <see cref="Upload"/>.
/// </summary>
public static class UploadExt
{
    private const string ChatEntryAttachmentTagPrefix = nameof(ChatEntryAttachment) + "/v1/";

    public static string BuildTag(ChatId chatId)
        => ChatEntryAttachmentTagPrefix + chatId.Value;

    public static bool HasChatEntryAttachmentTag(this Upload upload)
        => upload.Tag.StartsWith(ChatEntryAttachmentTagPrefix);

    public static ChatId ExtractChatIdFromTag(this Upload upload)
    {
        var parts = upload.Tag.Split('/');
        if (parts is [nameof(ChatEntryAttachment), "v1", _]
            && ChatId.TryParse(parts[2], out var chatId))
            return chatId;

        throw StandardError.Constraint("Invalid upload tag.");
    }
}
