namespace ActualChat.Chat;

public static class ChatServiceExt
{
    public static ChatEntryReader CreateEntryReader(
        this IChatService chats,
        ChatId chatId, Session session)
        => new(chats) {
            ChatId = chatId,
            Session = session,
        };
}
