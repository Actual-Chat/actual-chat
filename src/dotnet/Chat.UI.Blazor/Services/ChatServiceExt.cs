namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatServiceExt
{
    public static ChatEntryReader CreateEntryReader(
        this IChats chats,
        Session session,
        ChatId chatId)
        => new(chats) {
            Session = session,
            ChatId = chatId,
        };
}
