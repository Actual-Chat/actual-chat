namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatSummaryExt
{
    public const int MaxUnreadChatCount = 1000;

    public static Trimmed<int> UnreadMessageCount(this IEnumerable<ChatSummary> chats)
        => chats
            .Select(c => c.UnreadMessageCount)
            .Sum();

    public static Trimmed<int> UnreadChatCount(this IEnumerable<ChatSummary> chats)
        => chats
            .Select(c => new Trimmed<int>(c.UnreadMessageCount.Value > 0 ? 1 : 0, MaxUnreadChatCount))
            .Sum();
}
