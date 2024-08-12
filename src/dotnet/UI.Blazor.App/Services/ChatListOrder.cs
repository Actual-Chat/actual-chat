namespace ActualChat.UI.Blazor.App.Services;

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
            ChatListOrder.ByOwnUpdateTime => "icon-sort-by-me",
            ChatListOrder.ByUnreadCount => "icon-sort-by-message",
            ChatListOrder.ByAlphabet => "icon-sort-by-alphabet",
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
}
