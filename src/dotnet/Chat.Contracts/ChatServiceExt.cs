namespace ActualChat.Chat;

public static class ChatServiceExt
{
    public static ChatEntryReader CreateEntryReader(
        this IChatService chats,
        Session session,
        ChatId chatId)
        => new(chats) {
            Session = session,
            ChatId = chatId,
        };
}
