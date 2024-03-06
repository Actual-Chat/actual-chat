namespace ActualChat.Search.IntegrationTests;

public static class ContactSearchResultUtil
{
    public static ContactSearchResult ToSearchResult(this Chat.Chat chat, UserId userId)
        => BuildSearchResult(userId, chat.Id, chat.Title);

    public static ContactSearchResult BuildSearchResult(UserId ownerId, ChatId chatId, string fullName)
        => new (new ContactId(ownerId, chatId), SearchMatch.New(fullName));
}
