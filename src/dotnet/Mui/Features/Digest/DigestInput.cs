namespace ActualChat.Mui;

public static class DigestInput
{
    public static ChatId[] ParseChatIds(string? text)
        => (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ChatId.Parse)
            .ToArray();
}
