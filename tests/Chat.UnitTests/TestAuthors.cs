using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public static class TestAuthors
{
    public static Author Jack { get; } = New(new ChatId(Generate.Option), 123, "Jack");

    public static Author New(string chatId, int authorLid, string authorName)
        => new (new AuthorId(new ChatId(chatId), authorLid, AssumeValid.Option)) {
            Avatar = new Avatar {
                Name = authorName,
            },
        };
}
