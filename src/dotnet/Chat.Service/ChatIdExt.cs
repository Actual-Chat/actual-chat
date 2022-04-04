namespace ActualChat.Chat;

internal static class ChatIdExt
{
    private const string ChatIdPrefix = "direct_author:";

    public static bool IsDirectAuthorChatId(this string chatId)
        => ((Symbol)chatId).IsDirectAuthorChatId();

    public static bool IsDirectAuthorChatId(this Symbol chatId)
        => chatId.Value.StartsWith(ChatIdPrefix, StringComparison.Ordinal);

    public static bool IsAuthorsDirectChat(this string chatId)
        => chatId.Count(c => c == ':') == 2;

    public static Symbol CreateDirectAuthorChatId(this Symbol chatAuthorId)
        => ChatIdPrefix + chatAuthorId;

    public static Symbol ExtractAuthorId(this Symbol directAuthorChatId)
    {
        if (directAuthorChatId.IsDirectAuthorChatId())
            throw new InvalidOperationException();
        return directAuthorChatId.Value.Substring(ChatIdPrefix.Length);
    }
}
