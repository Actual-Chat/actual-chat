namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatListOrder
{
    ByLastEventTime = 0,
    ByOwnUpdateTime,
    ByUnreadCount,
    ByAlphabet,
}

public enum ChatListPreOrder
{
    ChatList = 0,
    None,
    NotesFirst
}

public static class ChatListOrderExt
{
    public static string GetIcon(this ChatListOrder order)
        => order switch {
            ChatListOrder.ByLastEventTime => "icon-sort-by-recent",
            ChatListOrder.ByOwnUpdateTime => "icon-sort-by-message",
            ChatListOrder.ByUnreadCount => "icon-sort-by-recent",
            ChatListOrder.ByAlphabet => "icon-sort-by-alphabet",
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
}
