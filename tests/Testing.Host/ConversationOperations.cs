namespace ActualChat.Testing.Host;

public static class ConversationOperations
{
    public static Task<Conversation> CreateConversation(
        this IWebTester tester,
        ChatEntry firstEntry,
        ChatEntry lastEntry,
        string summary,
        string title = "Conversation")
    {
        var chatId = firstEntry.ChatId;
        var conversation = new Conversation(ConversationId.New(chatId, firstEntry.LocalId), 1) {
            Title = title,
            Description = summary,
            Summary = summary,
            MessageCount = (int)(lastEntry.LocalId - firstEntry.LocalId + 1),
            EndEntryLid = lastEntry.LocalId,
            StartsAt = firstEntry.BeginsAt,
            EndsAt = lastEntry.BeginsAt,
        };
        return tester.Commander.Call(new ConversationBackend_Materialize(conversation));
    }
}
