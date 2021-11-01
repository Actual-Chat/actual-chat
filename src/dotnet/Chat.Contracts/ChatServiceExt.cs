namespace ActualChat.Chat;

public static class ChatServiceExt
{
    public static ChatEntryReader CreateEntryReader(
        this IChatServiceFacade chats,
        Session session,
        ChatId chatId)
        => new(chats) {
            Session = session,
            ChatId = chatId,
        };
}
