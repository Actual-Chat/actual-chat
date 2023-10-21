namespace ActualChat.Chat.UnitTests;

public static class TestAuthors
{
    public static readonly Author Jack = New(new ChatId(Generate.Option), 123, "Jack");

    public static Author New(string chatId, int authorLid, string authorName)
        => new (new AuthorId(new ChatId(chatId), authorLid, AssumeValid.Option)) {
            Avatar = new (Symbol.Empty) {
                Name = authorName,
            },
        };
}
