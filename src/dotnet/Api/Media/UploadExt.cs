namespace ActualChat.Media;

/// <summary>
/// Extension methods for <see cref="Upload"/>.
/// </summary>
public static class UploadExt
{
    public static string BuildTag(ChatId chatId)
        => nameof(ChatEntryAttachment) + "/v1/" + chatId.Value;

    public static ChatId ExtractChatIdFromTag(this Upload upload)
    {
        var parts = upload.Tag.Split('/');
        if (parts.Length == 3
            && parts[0] == nameof(ChatEntryAttachment)
            && parts[1] == "v1"
            && ChatId.TryParse(parts[2], out var chatId))
            return chatId;

        throw StandardError.Constraint("Invalid upload tag.");
    }
}
