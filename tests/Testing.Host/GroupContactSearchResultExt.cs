using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Testing.Host;

public static class GroupContactSearchResultExt
{
    public static List<FoundItem> BuildFoundContacts(
        this Account owner,
        bool isGlobalSearchResult,
        params IEnumerable<Chat.Chat> chats)
        => chats.Select(x => owner.BuildFoundContact(x, isGlobalSearchResult)).ToList();

    public static FoundItem BuildFoundContact(this Account owner, Chat.Chat chat, bool isGlobalSearchResult)
        => new (owner.BuildSearchResult(chat), SearchScope.Groups, isGlobalSearchResult);

    public static List<FoundContact> BuildSearchResults(this Account owner, params IEnumerable<Chat.Chat> chats)
        => chats.Select(x => BuildSearchResult(owner, x)).ToList();

    public static List<FoundContact> BuildSearchResults(this Account owner, params IEnumerable<(Chat.Chat Chat, Range<int>[]? SearchMatchPartRanges)> chats)
        => chats.Select(x => BuildSearchResult(owner, x.Chat, x.SearchMatchPartRanges)).ToList();

    public static FoundContact BuildSearchResult(this Account owner, Chat.Chat chat, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(chat, owner.Id, "", searchMatchPartRanges);

    public static FoundContact BuildSearchResult(this Account owner, Chat.Chat chat, string testIsolationKey, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(chat, owner.Id, testIsolationKey, searchMatchPartRanges);

    public static FoundContact BuildSearchResult(
        this Chat.Chat chat,
        UserId userId,
        Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, chat.Id, chat.Title, "", searchMatchPartRanges);

    public static FoundContact BuildSearchResult(
        this Chat.Chat chat,
        UserId userId,
        string testIsolationKey,
        Range<int>[]? searchMatchPartRanges)
        => BuildSearchResult(userId, chat.Id, chat.Title, testIsolationKey, searchMatchPartRanges);

    public static FoundContact BuildSearchResult(this UserId ownerId, ChatId chatId, string title, string testIsolationKey = "", Range<int>[]? searchMatchPartRanges = null)
        => new (ContactId.NewAny(ownerId, chatId), searchMatchPartRanges.BuildSearchMatch(title, testIsolationKey));
}
