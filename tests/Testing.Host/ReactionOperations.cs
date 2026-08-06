
namespace ActualChat.Testing.Host;

public static class ReactionOperations
{
    public static Task React(this IWebClientTester tester, ChatEntryId entryId, Emoji emoji)
    {
        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = entryId,
            Emoji = emoji,
        };
        return tester.Commander.Call(new Reactions_React(tester.Session, reaction));
    }
}
