using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class ChatEntryOperations
{
    public static Task<ChatEntry> CreateTextEntry(this IWebClientTester tester, ChatId chatId, string text)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, chatId, null, text));

    public static Task<ChatEntry> UpdateTextEntry(this IWebClientTester tester, ChatEntryId id, string text)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, id.ChatId, id.LocalId, text));
}
