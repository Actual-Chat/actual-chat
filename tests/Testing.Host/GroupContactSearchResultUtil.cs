using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class GroupContactSearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundContacts(
        this Account owner,
        params Chat.Chat[] chats)
        => chats.Select(owner.BuildFoundContact);

    public static FoundItem BuildFoundContact(this Account owner, Chat.Chat chat)
        => new (owner.BuildSearchResult(chat), SearchScope.Groups);

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params Chat.Chat[] chats)
        => chats.Select(x => BuildSearchResult(owner, x));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params (Chat.Chat Chat, Range<int>[]? SearchMatchPartRanges)[] chats)
        => chats.Select(x => BuildSearchResult(owner, x.Chat, x.SearchMatchPartRanges));

    public static ContactSearchResult BuildSearchResult(this Account owner, Chat.Chat chat, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(chat, owner.Id, "", searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Account owner, Chat.Chat chat, string uniquePart, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(chat, owner.Id, uniquePart, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(
        this Chat.Chat chat,
        UserId userId,
        Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, chat.Id, chat.Title, "", searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(
        this Chat.Chat chat,
        UserId userId,
        string uniquePart,
        Range<int>[]? searchMatchPartRanges)
        => BuildSearchResult(userId, chat.Id, chat.Title, uniquePart, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, ChatId chatId, string title, string uniquePart = "", Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, chatId), searchMatchPartRanges.BuildSearchMatch(title, uniquePart));
}
