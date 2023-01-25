namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatListOrder
{
    ByLastEventTime = 0,
    ByOwnUpdateTime,
    ByUnreadCount,
    ByAlphabet,
}

public static class ChatListOrderExt
{
    public static string GetIcon(this ChatListOrder order)
        => order switch {
            ChatListOrder.ByOwnUpdateTime => "icon-sort-by-message",
            ChatListOrder.ByLastEventTime => "icon-sort-by-recent",
            ChatListOrder.ByUnreadCount => "icon-sort-by-recent",
            ChatListOrder.ByAlphabet => "icon-sort-by-alphabet",
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
}
