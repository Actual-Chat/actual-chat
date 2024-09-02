using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class ChatEntryOperations
{
    public static Task<ChatEntry> CreateTextEntry(
        this IWebTester tester,
        ChatId chatId,
        string text,
        MediaId mediaId = default)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, chatId, null, text) {
            EntryAttachments = mediaId.IsNone
                ? []
                : ApiArray.New(new TextEntryAttachment {
                    MediaId = mediaId,
                    Index = 0,
                }),
        });

    public static Task<ChatEntry> UpdateTextEntry(this IWebTester tester, ChatEntryId id, string text)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, id.ChatId, id.LocalId, text));

    public static Task RemoveTextEntry(this IWebTester tester, ChatEntryId id)
        => tester.Commander.Call(new Chats_RemoveTextEntry(tester.Session, id.ChatId, id.LocalId));
}
