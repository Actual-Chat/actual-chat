using ActualChat.Users;

namespace ActualChat.Chat;

public sealed record ChatAuthor : Author
{
    public Symbol ChatId { get; init; }
    public Symbol UserId { get; init; }

    public static bool TryGetChatId(string chatAuthorId, out string chatId)
    {
        chatId = "";
        if (string.IsNullOrEmpty(chatAuthorId))
            return false;
        var chatIdLength = chatAuthorId.IndexOf(":", StringComparison.Ordinal);
        if (chatIdLength == -1)
            return false;
        chatId = chatAuthorId.Substring(0, chatIdLength);
        return true;
    }
}
