using ActualChat.Search;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class ContactSearchResultUtil
{
    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account account, params Chat.Chat[] chats)
        => chats.Select(x => x.BuildSearchResult(account.Id));

    public static ContactSearchResult BuildSearchResult(this Chat.Chat chat, UserId userId)
        => BuildSearchResult(userId, chat.Id, chat.Title);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, ChatId chatId, string fullName)
        => new (new ContactId(ownerId, chatId), SearchMatch.New(fullName));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params AccountFull[] others)
        => others.Select(owner.BuildSearchResult);

    public static ContactSearchResult BuildSearchResult(this Account owner, AccountFull other)
        => owner.Id.BuildSearchResult(other.Id, other.FullName);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, UserId otherUserId, string fullName)
        => new (new ContactId(ownerId, new PeerChatId(ownerId, otherUserId).ToChatId()), SearchMatch.New(fullName));
}
