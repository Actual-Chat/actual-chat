namespace ActualChat.Chat.UnitTests;

public static class TestAuthors
{
    public static readonly Author Jack = New(GroupChatId.New(), 123, "Jack");

    public static Author New(ChatId chatId, int authorLid, string authorName)
        => new (AuthorId.New(chatId, authorLid)) {
            Avatar = new (Symbol.Empty) {
                Name = authorName,
            },
        };
}
