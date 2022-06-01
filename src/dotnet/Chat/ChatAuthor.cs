using ActualChat.Users;

namespace ActualChat.Chat;

public sealed record ChatAuthor : Author
{
    public Symbol ChatId { get; init; }
    public Symbol UserId { get; init; }

    public static bool TryGetChatId(string chatAuthorId, out string chatId)
    {
        chatId = "";
        if (chatAuthorId.IsNullOrEmpty())
            return false;
        var chatIdLength = chatAuthorId.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;
        chatId = chatAuthorId.Substring(0, chatIdLength);
        return true;
    }

    public static bool TryParse(string chatAuthorId, out string chatId, out long localId)
    {
        chatId = "";
        localId = 0;
        if (chatAuthorId.IsNullOrEmpty())
            return false;
        var chatIdLength = chatAuthorId.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;
        chatId = chatAuthorId.Substring(0, chatIdLength);
        var localIdStr = chatAuthorId.Substring(chatIdLength + 1);
        return long.TryParse(localIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out localId);
    }

    public static void Parse(string chatAuthorId, out string chatId, out long localId)
    {
        if (!TryParse(chatAuthorId, out chatId, out localId))
            throw new FormatException("Invalid chat author ID format.");
    }

    public static bool IsValidId(string chatAuthorId)
        => TryParse(chatAuthorId, out var chatId, out _) && Chat.IsValidId(chatId);
}
