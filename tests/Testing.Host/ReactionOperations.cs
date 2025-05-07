using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class ReactionOperations
{
    public static Task React(this IWebClientTester tester, TextEntryId entryId, AuthorId authorId, Emoji emoji)
        => tester.Commander.Call(new Reactions_React(tester.Session, new Reaction {
            EntryId = entryId,
            AuthorId = authorId,
            EmojiId = emoji,
        }));
}
