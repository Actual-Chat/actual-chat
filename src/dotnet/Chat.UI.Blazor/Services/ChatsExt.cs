namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatsExt
{
    public static ChatEntryReader NewEntryReader(
        this IChats chats,
        Session session,
        string chatId,
        ChatEntryType entryType)
        => new(chats, session, chatId, entryType);
}
