namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatStateExt
{
    public const int MaxUnreadChatCount = 1000;

    public static Trimmed<int> UnreadMessageCount(this IEnumerable<ChatState> chats)
        => chats
            .Select(c => c.UnreadMessageCount)
            .Sum();

    public static Trimmed<int> UnreadChatCount(this IEnumerable<ChatState> chats)
        => chats
            .Select(c => new Trimmed<int>(c.UnreadMessageCount.Value > 0 ? 1 : 0, MaxUnreadChatCount))
            .Sum();
}
